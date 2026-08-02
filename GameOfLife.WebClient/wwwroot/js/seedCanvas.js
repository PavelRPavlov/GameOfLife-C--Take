// Seed-editor authoring canvas (wayfinder #18, folding in #16). A large *fixed* 100x100 painting
// surface — NOT the pan/zoom torus viewport (gameViewport.js). There is deliberately no pan and no
// zoom here: while you author a seed the grid stays put and shows all 100 rows/columns at once, so a
// click always lands on the cell under the cursor. C# (SeedBoard) stays the authoritative bitmap;
// this module only renders it and reports pointer strokes back:
//
//   left button   -> paint  (cell becomes alive)      drag to sweep a line of live cells
//   right button  -> erase  (cell becomes dead)       drag to sweep them out
//   per stroke    -> setBit locally + optimistic fillRect + invoke OnPaint(x,y,alive)  (no lag)
//   stroke end    -> repaint (clean lattice) + invoke OnStrokeEnd  (C# refreshes alive count)
//   bulk op (stamp/RLE/clear/invert/random) -> C# calls render(id, packedBytes) to resync the view
//
// A JS-side copy of the bitmap (`bits`) exists only so a stroke can repaint clean gridlines without a
// round-trip; C# remains the source of truth via the OnPaint callbacks.
//
// Sizing: the canvas element is sized by CSS (a viewport-relative square). This module measures that
// box via a ResizeObserver, sets the backing store to the device-pixel resolution, and derives the
// per-cell size `px = cssWidth / n`. Every draw runs under a dpr transform so all geometry is in CSS
// pixels. Bigger box -> bigger cells; the whole 100x100 grid always fits because px is derived from it.

const boards = new Map();
let nextId = 1;

// Shared with the live torus viewport (gameViewport.js) so the authoring grid and the running grid
// read as one surface: dark backdrop, green cells, a faint lattice.
const ALIVE = '#7cf29c';
const DEAD = '#0b0f1a';
const GRID = '#182135';        // minor lattice: one line per cell
const GRID_MAJOR = '#2a3a5c';  // brighter line every 10 cells, for orientation
const MIN_GRID_PX = 4;         // below this cell size the per-cell lattice is skipped (unreadable)

export function create(canvas, dotnet, n) {
    const id = nextId++;
    const s = {
        id,
        canvas,
        ctx: canvas.getContext('2d'),
        dotnet,
        n,
        px: 1,   // CSS pixels per cell, derived from the measured box in resize()
        dpr: 1,
        mode: 0, // 0 = idle, 1 = painting (left), 2 = erasing (right)
        bits: new Uint8Array((n * n) / 8),
    };
    boards.set(id, s);

    const cellAt = (e) => {
        const r = canvas.getBoundingClientRect();
        return [
            Math.floor((e.clientX - r.left) / s.px),
            Math.floor((e.clientY - r.top) / s.px),
        ];
    };

    const stroke = (x, y, alive) => {
        if (x < 0 || x >= n || y < 0 || y >= n) return;
        setBit(s, x, y, alive);
        drawCell(s, x, y, alive);
        s.dotnet.invokeMethodAsync('OnPaint', x, y, alive);
    };

    // Right-click paints-out rather than opening the browser menu, so suppress the context menu.
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());

    canvas.addEventListener('pointerdown', (e) => {
        // Left button paints alive, right button erases; ignore middle/other buttons.
        if (e.button === 0) s.mode = 1;
        else if (e.button === 2) s.mode = 2;
        else return;
        e.preventDefault();
        canvas.setPointerCapture(e.pointerId);
        const [x, y] = cellAt(e);
        stroke(x, y, s.mode === 1);
    });
    canvas.addEventListener('pointermove', (e) => {
        if (!s.mode) return;
        const [x, y] = cellAt(e);
        stroke(x, y, s.mode === 1);
    });
    const end = (e) => {
        if (!s.mode) return;
        s.mode = 0;
        try { canvas.releasePointerCapture(e.pointerId); } catch { /* already released */ }
        repaint(s); // restore the clean lattice the optimistic per-cell draws chewed through
        s.dotnet.invokeMethodAsync('OnStrokeEnd');
    };
    // Pointer capture keeps move/up flowing even outside the element, so no pointerleave handler is
    // needed (and adding one would abort a stroke that briefly grazes the edge).
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', end);

    resize(s);
    // ResizeObserver, not a window 'resize' listener: the canvas can gain its real size *after*
    // create() (deferred layout, a flex reflow, a hidden pane becoming visible). Re-measure on any
    // size change so px and the backing store track the CSS box.
    s._ro = new ResizeObserver(() => resize(s));
    s._ro.observe(canvas);
    return id;
}

// Resync the whole view to C#'s authoritative bitmap after a bulk operation.
export function render(id, packed) {
    const s = boards.get(id);
    if (!s) return;
    s.bits.set(packed);
    repaint(s);
}

export function dispose(id) {
    const s = boards.get(id);
    if (!s) return;
    s._ro?.disconnect();
    boards.delete(id);
}

// ---- internals ----------------------------------------------------------

function resize(s) {
    const r = s.canvas.getBoundingClientRect();
    if (r.width <= 0) return; // not laid out yet; the observer will fire again once it is
    s.dpr = window.devicePixelRatio || 1;
    s.px = r.width / s.n; // square box (CSS aspect-ratio 1/1), so width drives the per-cell size
    s.canvas.width = Math.max(1, Math.round(r.width * s.dpr));
    s.canvas.height = Math.max(1, Math.round(r.height * s.dpr));
    repaint(s);
}

function setBit(s, x, y, alive) {
    const i = y * s.n + x;
    const mask = 0x80 >> (i & 7);
    if (alive) s.bits[i >> 3] |= mask;
    else s.bits[i >> 3] &= ~mask;
}

function getBit(s, x, y) {
    const i = y * s.n + x;
    return (s.bits[i >> 3] >> (7 - (i & 7))) & 1;
}

// Optimistic single-cell paint during a stroke: fill the cell, then redraw its lattice border so the
// grid survives until the stroke-end repaint. Cheap enough to run on every pointermove.
function drawCell(s, x, y, alive) {
    const { ctx, px } = s;
    ctx.setTransform(s.dpr, 0, 0, s.dpr, 0, 0);
    ctx.fillStyle = alive ? ALIVE : DEAD;
    ctx.fillRect(x * px, y * px, px, px);
    if (px >= MIN_GRID_PX) {
        ctx.strokeStyle = GRID;
        ctx.lineWidth = 1;
        ctx.strokeRect(Math.round(x * px) + 0.5, Math.round(y * px) + 0.5, Math.round(px), Math.round(px));
    }
}

function repaint(s) {
    const { ctx, n, px } = s;
    const W = n * px;
    ctx.setTransform(s.dpr, 0, 0, s.dpr, 0, 0);

    ctx.fillStyle = DEAD;
    ctx.fillRect(0, 0, W, W);

    ctx.fillStyle = ALIVE;
    for (let y = 0; y < n; y++)
        for (let x = 0; x < n; x++)
            if (getBit(s, x, y)) ctx.fillRect(x * px, y * px, px, px);

    // Per-cell lattice: one grid line per cell so a visible square == a single clickable cell. This is
    // the fix for "cells and gridlines were different sizes" — every square you see is one cell.
    if (px >= MIN_GRID_PX) {
        strokeLattice(ctx, n, px, W, 1, GRID);
    }
    // Brighter line every 10 cells to keep your bearings on the 100-wide field.
    strokeLattice(ctx, n, px, W, 10, GRID_MAJOR);
}

function strokeLattice(ctx, n, px, W, step, color) {
    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let i = 0; i <= n; i += step) {
        const p = Math.round(i * px) + 0.5; // +0.5 so a 1px line lands on a device pixel, not blurred
        ctx.moveTo(p, 0); ctx.lineTo(p, W);
        ctx.moveTo(0, p); ctx.lineTo(W, p);
    }
    ctx.stroke();
}
