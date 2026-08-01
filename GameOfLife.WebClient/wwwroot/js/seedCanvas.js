// Seed-editor authoring canvas (wayfinder #18, folding in #16). A small fixed 100x100 painting
// surface — NOT the pan/zoom torus viewport (gameViewport.js). C# (SeedBoard) stays the authoritative
// bitmap; this module only renders it and reports pointer strokes back:
//
//   pointer stroke      -> setBit locally + optimistic fillRect + invoke OnPaint(x,y,alive)  (no lag)
//   stroke end          -> repaint (clean gridlines) + invoke OnStrokeEnd  (C# refreshes alive count)
//   bulk op (stamp/RLE/clear/invert/random) -> C# calls render(id, packedBytes) to resync the view
//
// A JS-side copy of the bitmap (`bits`) exists only so a stroke can repaint clean gridlines without a
// round-trip; C# remains the source of truth via the OnPaint callbacks.

const boards = new Map();
let nextId = 1;

const ALIVE = '#1b6ec2';
const DEAD = '#ffffff';
const GRID = 'rgba(120,120,120,0.28)';

export function create(canvas, dotnet, n, px) {
    const id = nextId++;
    const s = {
        canvas,
        ctx: canvas.getContext('2d'),
        dotnet,
        n,
        px,
        tool: 'paint',
        down: false,
        bits: new Uint8Array((n * n) / 8),
    };
    boards.set(id, s);

    const cellAt = (e) => {
        const r = canvas.getBoundingClientRect();
        return [
            Math.floor((e.clientX - r.left) / px),
            Math.floor((e.clientY - r.top) / px),
        ];
    };

    const stroke = (x, y) => {
        if (x < 0 || x >= n || y < 0 || y >= n) return;
        const alive = s.tool !== 'erase';
        setBit(s, x, y, alive);
        drawCell(s, x, y, alive);
        s.dotnet.invokeMethodAsync('OnPaint', x, y, alive);
    };

    canvas.addEventListener('pointerdown', (e) => {
        s.down = true;
        canvas.setPointerCapture(e.pointerId);
        const [x, y] = cellAt(e);
        stroke(x, y);
    });
    canvas.addEventListener('pointermove', (e) => {
        if (!s.down) return;
        const [x, y] = cellAt(e);
        stroke(x, y);
    });
    const end = () => {
        if (!s.down) return;
        s.down = false;
        repaint(s);
        s.dotnet.invokeMethodAsync('OnStrokeEnd');
    };
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointerleave', end);

    repaint(s);
    return id;
}

export function setTool(id, tool) {
    const s = boards.get(id);
    if (s) s.tool = tool;
}

// Resync the whole view to C#'s authoritative bitmap after a bulk operation.
export function render(id, packed) {
    const s = boards.get(id);
    if (!s) return;
    s.bits.set(packed);
    repaint(s);
}

export function dispose(id) {
    boards.delete(id);
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

function drawCell(s, x, y, alive) {
    s.ctx.fillStyle = alive ? ALIVE : DEAD;
    s.ctx.fillRect(x * s.px, y * s.px, s.px, s.px);
}

function repaint(s) {
    const { ctx, n, px } = s;
    ctx.fillStyle = DEAD;
    ctx.fillRect(0, 0, n * px, n * px);

    ctx.fillStyle = ALIVE;
    for (let y = 0; y < n; y++)
        for (let x = 0; x < n; x++)
            if (getBit(s, x, y)) ctx.fillRect(x * px, y * px, px, px);

    ctx.strokeStyle = GRID;
    ctx.lineWidth = 0.5;
    for (let g = 0; g <= n; g += 10) {
        ctx.beginPath();
        ctx.moveTo(g * px, 0);
        ctx.lineTo(g * px, n * px);
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(0, g * px);
        ctx.lineTo(n * px, g * px);
        ctx.stroke();
    }
}
