namespace GameOfLife.Core;

/// <summary>
/// The sparse "variant A" Game of Life engine. Live cells are held in a <see cref="HashSet{T}"/>
/// keyed by <see cref="Cell"/>; the 2^128-cell space is never materialized and density is bounded
/// by population. The engine has no ASP.NET dependency by design.
/// </summary>
/// <remarks>
/// The engine is the single writer of <see cref="Current"/>: each <see cref="Advance"/> computes
/// the next generation into a fresh immutable <see cref="Generation"/> and publishes it under a
/// lightweight lock, so readers always observe a fully-published generation. The heavy neighbour
/// computation runs outside the lock, so publishing never contends with it.
/// </remarks>
public sealed class GameEngine
{
    private readonly Lock _sync = new();
    private Generation _current;

    /// <summary>Creates an engine seeded with <paramref name="seed"/> at generation 0.</summary>
    public GameEngine(IEnumerable<Cell> seed, Rule rule)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(rule);

        Rule = rule;
        var live = new HashSet<Cell>(seed);
        _current = new Generation(0, live, [], []);
    }

    /// <summary>The rule this engine applies, fixed for its lifetime.</summary>
    public Rule Rule { get; }

    /// <summary>The most recently published generation. Safe to read from any thread.</summary>
    public Generation Current
    {
        get
        {
            lock (_sync)
                return _current;
        }
    }

    /// <summary>
    /// Computes the next generation, publishes it as the new <see cref="Current"/>, and returns it.
    /// Must be called by a single writer at a time.
    /// </summary>
    public Generation Advance()
    {
        // Single writer: only Advance mutates _current, so reading the previous generation here
        // needs no lock. The next generation is published under _sync so readers get a happens-before.
        var previous = _current;
        var next = ComputeNext(previous, Rule);
        lock (_sync)
            _current = next;
        return next;
    }

    private static Generation ComputeNext(Generation previous, Rule rule)
    {
        var live = previous.LiveCells;

        // Count live neighbours for every cell adjacent to a live cell. A dead cell can only be
        // born if it neighbours at least one live cell, so this dictionary covers every birth
        // candidate as well as the survival count of every live cell that has ≥1 live neighbour.
        var neighbourCounts = new Dictionary<Cell, int>(live.Count * 4);
        foreach (var cell in live)
        {
            foreach (var neighbour in Neighbours(cell))
            {
                neighbourCounts.TryGetValue(neighbour, out var count);
                neighbourCounts[neighbour] = count + 1;
            }
        }

        // A live cell with zero live neighbours never appears above, but still must be evaluated
        // for survival (e.g. an S0 rule). Give it an explicit count of 0.
        foreach (var cell in live)
            neighbourCounts.TryAdd(cell, 0);

        var nextLive = new HashSet<Cell>(live.Count);
        var births = new List<Cell>();
        var deaths = new List<Cell>();

        foreach (var (cell, count) in neighbourCounts)
        {
            var aliveNow = live.Contains(cell);
            var aliveNext = aliveNow ? rule.IsSurvival(count) : rule.IsBirth(count);

            if (aliveNext)
            {
                nextLive.Add(cell);
                if (!aliveNow) births.Add(cell);
            }
            else if (aliveNow)
            {
                deaths.Add(cell);
            }
        }

        return new Generation(previous.Number + 1, nextLive, births, deaths);
    }

    /// <summary>
    /// The 8 Moore neighbours of <paramref name="cell"/>. Wraparound is free — unchecked
    /// <see cref="ulong"/> addition of (ulong)(-1) == ulong.MaxValue is already mod 2^64.
    /// </summary>
    private static IEnumerable<Cell> Neighbours(Cell cell)
    {
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            yield return new Cell(
                unchecked(cell.X + (ulong)dx),
                unchecked(cell.Y + (ulong)dy));
        }
    }
}
