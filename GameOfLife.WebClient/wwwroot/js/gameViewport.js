// Game of Life viewport renderer (wayfinder #15).
//
// The chosen rendering approach (research #11): keep the live cell state JS-side, paint on
// requestAnimationFrame, and feed the viewport a *snapshot* once + *deltas* thereafter — never
// re-marshal the whole set per frame. This module is the reusable engine; a thin Blazor component
// (GameViewport.razor) forwards GameStore.SnapshotApplied / DeltaApplied into it, and the throwaway
// /proto/viewport page drives it from a fake feed.
//
// Coordinates are 64-bit-per-axis on the 2^64 x 2^64 torus, so a coordinate can exceed JS's 2^53
// safe-integer range. Cells are therefore held as BigInt and marshalled across interop as decimal
// strings ("x,y"). The paint loop relativizes every cell to the BigInt camera (cell - cam) and only
// Number()s the *small* result, so all pixel math stays in double-safe range. A naive cellX*cellSize
// would corrupt past 2^53; this never does.
//
// Viewport model (decided with Pavel on #15): fit-once-then-free camera (auto-frame the live set on
// the first snapshot only, never yank the camera on a resync), flat-plane culling (no torus-seam
// rendering — the seed is sparse and the wrap seam is ~1.8e19 cells away, never reached by panning).

const viewports = new Map(); // handle id -> state
let nextId = 1;

const MIN_CELL_PX = 1;
const MAX_CELL_PX = 40;
const BG = '#0b0f1a';
const GRID = '#182135';
const CELL = '#7cf29c';
const HUD = 'rgba(220,230,245,0.75)';

/** Create a viewport bound to a canvas element. Returns an integer handle for later calls. */
export function create(canvas) {
    const id = nextId++;
    const s = {
        id, canvas,
        ctx: canvas.getContext('2d'),
        cells: new Map(),   // "x,y" -> { x: BigInt, y: BigInt }
        camX: 0n, camY: 0n, // world cell at the screen's top-left
        subX: 0, subY: 0,   // sub-cell pixel scroll within [0, cellSize)
        cellSize: 12,       // pixels per cell (the zoom level)
        dpr: 1, viewW: 0, viewH: 0,
        dirty: true,
        fitPending: true,   // "fit once": consumed by the first good paint that has cells
        raf: 0,
        dragging: false, lastX: 0, lastY: 0,
        gen: 0, cellCount: 0,
    };
    viewports.set(id, s);
    resize(s);
    attachInput(s);
    const loop = () => { paint(s); s.raf = requestAnimationFrame(loop); };
    s.raf = requestAnimationFrame(loop);
    // ResizeObserver, not a window 'resize' listener: the canvas can gain its real size *after*
    // create() (deferred layout, a hidden pane becoming visible, a flex reflow). The observer
    // re-measures on any size change and the fit-once still lands on the first non-empty paint.
    s._ro = new ResizeObserver(() => resize(s));
    s._ro.observe(canvas);
    return id;
}

/** Replace the whole live set from a snapshot. keys: array of "x,y" decimal-string pairs. */
export function snapshot(id, keys, gen) {
    const s = viewports.get(id); if (!s) return;
    s.cells = new Map();
    for (const k of keys) s.cells.set(k, parseKey(k));
    s.gen = gen ?? s.gen;
    s.cellCount = s.cells.size;
    s.dirty = true;
}

/** Apply an incremental delta. births/deaths: arrays of "x,y". */
export function delta(id, births, deaths, gen) {
    const s = viewports.get(id); if (!s) return;
    for (const k of deaths) s.cells.delete(k);
    for (const k of births) s.cells.set(k, parseKey(k));
    s.gen = gen ?? s.gen;
    s.cellCount = s.cells.size;
    s.dirty = true;
}

/** Re-frame the camera to fit the current live set (the [Fit] button). */
export function fit(id) {
    const s = viewports.get(id); if (!s) return;
    doFit(s);
}

/** Tear down a viewport. */
export function dispose(id) {
    const s = viewports.get(id); if (!s) return;
    cancelAnimationFrame(s.raf);
    s._ro?.disconnect();
    viewports.delete(id);
}

// ---- internals ----------------------------------------------------------

function parseKey(k) {
    const i = k.indexOf(',');
    return { x: BigInt(k.slice(0, i)), y: BigInt(k.slice(i + 1)) };
}

function resize(s) {
    const r = s.canvas.getBoundingClientRect();
    s.dpr = window.devicePixelRatio || 1;
    s.viewW = r.width;
    s.viewH = r.height;
    s.canvas.width = Math.max(1, Math.round(r.width * s.dpr));
    s.canvas.height = Math.max(1, Math.round(r.height * s.dpr));
    s.dirty = true;
}

function doFit(s) {
    if (s.viewW <= 0) resize(s);
    if (s.cells.size === 0 || s.viewW <= 0) return;
    let minX, minY, maxX, maxY, first = true;
    for (const c of s.cells.values()) {
        if (first) { minX = maxX = c.x; minY = maxY = c.y; first = false; continue; }
        if (c.x < minX) minX = c.x; else if (c.x > maxX) maxX = c.x;
        if (c.y < minY) minY = c.y; else if (c.y > maxY) maxY = c.y;
    }
    const spanX = Number(maxX - minX) + 1;
    const spanY = Number(maxY - minY) + 1;
    const pad = 2;
    const csX = s.viewW / (spanX + pad * 2);
    const csY = s.viewH / (spanY + pad * 2);
    s.cellSize = clamp(Math.floor(Math.min(csX, csY)), MIN_CELL_PX, MAX_CELL_PX);
    // Anchor the camera at the bbox min, then push the content to screen-centre via the sub-cell
    // offset. screenX(cell) = Number(cell.x - camX)*cellSize - subX, so subX = -margin centres it.
    const marginX = (s.viewW - spanX * s.cellSize) / 2;
    const marginY = (s.viewH - spanY * s.cellSize) / 2;
    s.camX = minX; s.camY = minY;
    s.subX = -marginX; s.subY = -marginY;
    s.dirty = true;
}

function paint(s) {
    if (s.fitPending && s.cells.size > 0 && s.viewW > 0) { doFit(s); s.fitPending = false; }
    if (!s.dirty) return;
    s.dirty = false;

    const ctx = s.ctx, cs = s.cellSize;
    ctx.setTransform(s.dpr, 0, 0, s.dpr, 0, 0);
    ctx.fillStyle = BG;
    ctx.fillRect(0, 0, s.viewW, s.viewH);

    // Grid lines, only when cells are big enough to read as a lattice.
    if (cs >= 8) {
        ctx.strokeStyle = GRID;
        ctx.lineWidth = 1;
        ctx.beginPath();
        const ox = mod(-s.subX, cs), oy = mod(-s.subY, cs);
        for (let x = ox; x <= s.viewW; x += cs) { ctx.moveTo(x + 0.5, 0); ctx.lineTo(x + 0.5, s.viewH); }
        for (let y = oy; y <= s.viewH; y += cs) { ctx.moveTo(0, y + 0.5); ctx.lineTo(s.viewW, y + 0.5); }
        ctx.stroke();
    }

    // Cells. Cull to the visible window in cell space, then Number() only the small relative coord.
    const cols = Math.ceil(s.viewW / cs) + 2;
    const rows = Math.ceil(s.viewH / cs) + 2;
    const size = cs >= 4 ? cs - 1 : cs; // 1px gap once cells are big enough to show it
    ctx.fillStyle = CELL;
    for (const c of s.cells.values()) {
        const relX = Number(c.x - s.camX);
        if (relX < -1 || relX > cols) continue;
        const relY = Number(c.y - s.camY);
        if (relY < -1 || relY > rows) continue;
        ctx.fillRect(relX * cs - s.subX, relY * cs - s.subY, size, size);
    }

    drawHud(s);
}

function drawHud(s) {
    const lines = [
        `gen ${s.gen}   cells ${s.cellCount}   zoom ${s.cellSize}px`,
        `cam x ${s.camX.toString()}`,
        `cam y ${s.camY.toString()}`,
    ];
    s.ctx.font = '12px ui-monospace, SFMono-Regular, Menlo, monospace';
    s.ctx.textBaseline = 'bottom';
    let y = s.viewH - 6;
    for (let i = lines.length - 1; i >= 0; i--) {
        s.ctx.fillStyle = HUD;
        s.ctx.fillText(lines[i], 8, y);
        y -= 15;
    }
}

function attachInput(s) {
    const canvas = s.canvas;
    canvas.addEventListener('pointerdown', e => {
        s.dragging = true; s.lastX = e.clientX; s.lastY = e.clientY;
        canvas.setPointerCapture(e.pointerId);
    });
    canvas.addEventListener('pointermove', e => {
        if (!s.dragging) return;
        pan(s, e.clientX - s.lastX, e.clientY - s.lastY);
        s.lastX = e.clientX; s.lastY = e.clientY;
    });
    const end = e => { s.dragging = false; try { canvas.releasePointerCapture(e.pointerId); } catch { } };
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', end);
    canvas.addEventListener('wheel', e => {
        e.preventDefault();
        const r = canvas.getBoundingClientRect();
        zoom(s, e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? 1.1 : 1 / 1.1);
    }, { passive: false });
}

// Drag: move content with the cursor. screenX = rel*cs - subX, so to shift content by +dx we
// decrease subX; whole-cell overflow folds into the BigInt camera, keeping screenX invariant.
function pan(s, dx, dy) {
    s.subX -= dx; s.subY -= dy;
    normalize(s);
    s.dirty = true;
}

// Zoom about the cursor: hold the world point under (mx,my) fixed by solving the new sub-cell offset
// for the same rel = (m + sub)/cellSize. The BigInt camera is untouched, so this is precision-safe.
function zoom(s, mx, my, factor) {
    const oldCs = s.cellSize;
    const newCs = clamp(oldCs * factor, MIN_CELL_PX, MAX_CELL_PX);
    if (newCs === oldCs) return;
    s.subX = (mx + s.subX) * (newCs / oldCs) - mx;
    s.subY = (my + s.subY) * (newCs / oldCs) - my;
    s.cellSize = newCs;
    normalize(s);
    s.dirty = true;
}

// Fold whole-cell scroll out of subX/subY into the BigInt camera so sub stays in [0, cellSize)
// and rel coords stay small. Each step preserves every cell's screen position exactly.
function normalize(s) {
    const cs = s.cellSize;
    while (s.subX >= cs) { s.subX -= cs; s.camX += 1n; }
    while (s.subX < 0) { s.subX += cs; s.camX -= 1n; }
    while (s.subY >= cs) { s.subY -= cs; s.camY += 1n; }
    while (s.subY < 0) { s.subY += cs; s.camY -= 1n; }
}

function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }
function mod(a, n) { return ((a % n) + n) % n; }
