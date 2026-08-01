using GameOfLife.Core;
using GameOfLife.Core.Tests.Support;

namespace GameOfLife.Core.Tests.Drivers;

/// <summary>
/// Drives the sole actor of the pattern-evolution vertical: a <see cref="GameEngine"/> world. Owns
/// the engine and the glider's seam-straddling origin (state a When seeds and a Then reads back),
/// and exposes only actions plus the observable live set — the assertions stay in the steps.
/// Reqnroll context-injects one per scenario; the engine holds no unmanaged resources, so there is
/// no teardown.
/// </summary>
public sealed class WorldDriver
{
    private GameEngine? _engine;

    private GameEngine Engine =>
        _engine ?? throw new InvalidOperationException("World not seeded; seed it before advancing or reading cells.");

    /// <summary>The top-left origin the glider was seeded at, for computing its translated position.</summary>
    public Cell GliderOrigin { get; private set; }

    /// <summary>The current live set of the world.</summary>
    public IReadOnlySet<Cell> LiveCells => Engine.Current.LiveCells;

    /// <summary>Creates the world with the given rule and seed. Throws <see cref="FormatException"/> for a bad rule.</summary>
    public void CreateWorld(string rule, IEnumerable<Cell> seed) =>
        _engine = new GameEngine(seed, Rule.Parse(rule));

    /// <summary>Creates an empty world with the given rule (also the path that exercises rule rejection).</summary>
    public void CreateWorld(string rule) => CreateWorld(rule, []);

    public void SeedHorizontalBlinker(string rule, ulong cx, ulong cy) =>
        CreateWorld(rule, Patterns.HorizontalBlinker(cx, cy));

    public void SeedBlock(string rule, ulong x, ulong y) =>
        CreateWorld(rule, Patterns.Block(x, y));

    /// <summary>Seeds a glider at the torus origin corner so it straddles the seam on both axes.</summary>
    public void SeedGliderAtCorner(string rule)
    {
        GliderOrigin = new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1);
        CreateWorld(rule, Patterns.GliderAt(GliderOrigin, 0, 0));
    }

    /// <summary>Advances the world by the given number of generations.</summary>
    public void Advance(int generations)
    {
        for (var i = 0; i < generations; i++)
            Engine.Advance();
    }
}
