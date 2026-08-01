using GameOfLife.Core;

namespace GameOfLife.Core.Tests.Support;

/// <summary>
/// Builders for the known Conway patterns used across the pattern-evolution scenarios: cell sets
/// positioned on the 2^64 torus. Pure value builders (no engine), so steps, drivers, and plain
/// xUnit tests can all reuse the exact same shapes.
/// </summary>
public static class Patterns
{
    // A glider (moving down-right, +1/+1 every 4 generations), cells relative to its top-left.
    private static readonly (int dx, int dy)[] GliderOffsets =
    [
        (1, 0),
        (2, 1),
        (0, 2), (1, 2), (2, 2),
    ];

    /// <summary>Three cells in a row centred at (cx, cy).</summary>
    public static Cell[] HorizontalBlinker(ulong cx, ulong cy) =>
    [
        new(unchecked(cx - 1), cy),
        new(cx, cy),
        new(unchecked(cx + 1), cy),
    ];

    /// <summary>Three cells in a column centred at (cx, cy) — the blinker's period-2 phase.</summary>
    public static Cell[] VerticalBlinker(ulong cx, ulong cy) =>
    [
        new(cx, unchecked(cy - 1)),
        new(cx, cy),
        new(cx, unchecked(cy + 1)),
    ];

    /// <summary>A 2x2 still-life block with top-left at (x, y).</summary>
    public static Cell[] Block(ulong x, ulong y) =>
    [
        new(x, y), new(unchecked(x + 1), y),
        new(x, unchecked(y + 1)), new(unchecked(x + 1), unchecked(y + 1)),
    ];

    /// <summary>A glider with top-left at <paramref name="origin"/>, translated by (dx, dy) with torus wraparound.</summary>
    public static IEnumerable<Cell> GliderAt(Cell origin, ulong dx, ulong dy) =>
        GliderOffsets.Select(o => new Cell(
            unchecked(origin.X + (ulong)o.dx + dx),
            unchecked(origin.Y + (ulong)o.dy + dy)));
}
