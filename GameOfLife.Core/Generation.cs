namespace GameOfLife.Core;

/// <summary>
/// An immutable snapshot of the world at one generation. Carries <em>both</em> the full live
/// set (for late-join bootstrap) <em>and</em> the births/deaths delta versus the prior
/// generation (for steady-state notification) — the two things the notification strategy
/// actually transmits.
/// </summary>
public sealed class Generation
{
    internal Generation(
        long number,
        IReadOnlySet<Cell> liveCells,
        IReadOnlyCollection<Cell> births,
        IReadOnlyCollection<Cell> deaths)
    {
        Number = number;
        LiveCells = liveCells;
        Births = births;
        Deaths = deaths;
    }

    /// <summary>Generation number. The seed is generation 0.</summary>
    public long Number { get; }

    /// <summary>The full set of live cells at this generation.</summary>
    public IReadOnlySet<Cell> LiveCells { get; }

    /// <summary>Cells that came alive since the previous generation (empty at generation 0).</summary>
    public IReadOnlyCollection<Cell> Births { get; }

    /// <summary>Cells that died since the previous generation (empty at generation 0).</summary>
    public IReadOnlyCollection<Cell> Deaths { get; }
}
