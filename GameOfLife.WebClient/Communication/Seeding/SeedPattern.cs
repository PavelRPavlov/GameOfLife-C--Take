namespace GameOfLife.WebClient.Communication.Seeding;

/// <summary>
/// A named seed pattern in board-local coordinates (0-based, top-left origin). The create form's
/// preset library (wayfinder #16) stamps these onto the <see cref="SeedBoard"/>. Coordinates are
/// authoring-grid <c>(x = col, y = row)</c>, matching <see cref="SeedBoard"/>'s own convention.
/// </summary>
public sealed record SeedPattern(string Name, string Category, IReadOnlyList<(int X, int Y)> Cells)
{
    /// <summary>Width of the pattern's bounding box (0 for an empty pattern).</summary>
    public int Width { get; } = Cells.Count == 0 ? 0 : Cells.Max(c => c.X) + 1;

    /// <summary>Height of the pattern's bounding box (0 for an empty pattern).</summary>
    public int Height { get; } = Cells.Count == 0 ? 0 : Cells.Max(c => c.Y) + 1;
}

/// <summary>
/// The canonical preset library offered by the seed editor (#16's resolved set): one of each family
/// — still life, oscillators, spaceships, and a gun — so the interesting seeds are one click away.
/// </summary>
public static class SeedPatterns
{
    public static IReadOnlyList<SeedPattern> All { get; } =
    [
        new SeedPattern("Gosper gun x 2", "gun", GunsRow(2)),
        new SeedPattern("Glider", "spaceship", [(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)]),
        new SeedPattern("LWSS", "spaceship",
            [(1, 0), (4, 0), (0, 1), (0, 2), (4, 2), (0, 3), (1, 3), (2, 3), (3, 3)]),
        new SeedPattern("Pulsar", "oscillator", Pulsar()),
        new SeedPattern("Gosper gun", "gun", Gun()),
    ];

    // The pulsar is symmetric: six-cell bars mirrored across both diagonals. Build one quadrant's
    // bars at rows {0,5,7,12} over columns {2,3,4,8,9,10}, mirror by swapping (x,y), de-dupe.
    private static IReadOnlyList<(int X, int Y)> Pulsar()
    {
        int[] bar = [2, 3, 4, 8, 9, 10];
        int[] lines = [0, 5, 7, 12];
        var set = new HashSet<(int, int)>();
        foreach (var line in lines)
            foreach (var c in bar)
            {
                set.Add((c, line));
                set.Add((line, c));
            }
        return [.. set];
    }

    // <paramref name="count"/> Gosper guns side by side on a single row, each in its own 44-cell slot
    // (the 36-wide gun plus an 8-cell gap). Every gun fires its gliders down and to the right, straight off
    // the shared row, so row-mates never cross each other's stream before it wraps.
    private static IReadOnlyList<(int X, int Y)> GunsRow(int count)
    {
        const int tileW = 44; // 36-wide gun + 8-cell horizontal gap
        var gun = Gun();
        var cells = new List<(int X, int Y)>(gun.Count * count);
        for (var i = 0; i < count; i++)
        {
            var ox = i * tileW;
            foreach (var (x, y) in gun) cells.Add((ox + x, y));
        }
        return cells;
    }

    private static IReadOnlyList<(int X, int Y)> Gun() =>
    [
        (24, 0), (22, 1), (24, 1), (12, 2), (13, 2), (20, 2), (21, 2), (34, 2), (35, 2),
        (11, 3), (15, 3), (20, 3), (21, 3), (34, 3), (35, 3),
        (0, 4), (1, 4), (10, 4), (16, 4), (20, 4), (21, 4),
        (0, 5), (1, 5), (10, 5), (14, 5), (16, 5), (17, 5), (22, 5), (24, 5),
        (10, 6), (16, 6), (24, 6), (11, 7), (15, 7), (12, 8), (13, 8),
    ];
}
