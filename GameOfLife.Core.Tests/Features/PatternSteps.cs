using GameOfLife.Core;
using Reqnroll;

namespace GameOfLife.Core.Tests.Features;

[Binding]
public sealed class PatternSteps
{
    // A glider (moving down-right, +1/+1 every 4 generations), cells relative to its top-left.
    private static readonly (int dx, int dy)[] GliderOffsets =
    [
        (1, 0),
        (2, 1),
        (0, 2), (1, 2), (2, 2),
    ];

    private GameEngine _engine = null!;
    private Cell _gliderOrigin;

    [Given(@"a ""(.*)"" world seeded with a horizontal blinker centred at \((\d+), (\d+)\)")]
    public void GivenHorizontalBlinker(string rule, ulong cx, ulong cy)
    {
        _engine = new GameEngine(HorizontalBlinker(cx, cy), Rule.Parse(rule));
    }

    [Given(@"a ""(.*)"" world seeded with a glider at the torus origin corner")]
    public void GivenGliderAtCorner(string rule)
    {
        // Origin placed so the glider straddles the seam on both axes.
        _gliderOrigin = new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1);
        _engine = new GameEngine(GliderAt(_gliderOrigin, 0, 0), Rule.Parse(rule));
    }

    [Given(@"a ""(.*)"" world seeded with a 2x2 block at \((\d+), (\d+)\)")]
    public void GivenBlock(string rule, ulong x, ulong y)
    {
        _engine = new GameEngine(Block(x, y), Rule.Parse(rule));
    }

    [Given(@"a ""(.*)"" world seeded with no live cells")]
    public void GivenEmpty(string rule)
    {
        _engine = new GameEngine([], Rule.Parse(rule));
    }

    [When(@"the world advances (\d+) generation(?:s)?")]
    public void WhenAdvance(int generations)
    {
        for (var i = 0; i < generations; i++)
            _engine.Advance();
    }

    [Then(@"the live cells are a horizontal blinker centred at \((\d+), (\d+)\)")]
    public void ThenHorizontalBlinker(ulong cx, ulong cy)
    {
        Assert.Equal(HorizontalBlinker(cx, cy).ToHashSet(), _engine.Current.LiveCells);
    }

    [Then(@"the live cells are a vertical blinker centred at \((\d+), (\d+)\)")]
    public void ThenVerticalBlinker(ulong cx, ulong cy)
    {
        var expected = new HashSet<Cell>
        {
            new(cx, unchecked(cy - 1)),
            new(cx, cy),
            new(cx, unchecked(cy + 1)),
        };
        Assert.Equal(expected, _engine.Current.LiveCells);
    }

    [Then(@"the glider has translated by \((\d+), (\d+)\) with wraparound")]
    public void ThenGliderTranslated(ulong dx, ulong dy)
    {
        var expected = GliderAt(_gliderOrigin, dx, dy).ToHashSet();
        Assert.Equal(expected, _engine.Current.LiveCells);
    }

    [Then(@"the live cells are exactly the 2x2 block at \((\d+), (\d+)\)")]
    public void ThenBlock(ulong x, ulong y)
    {
        Assert.Equal(Block(x, y).ToHashSet(), _engine.Current.LiveCells);
    }

    [Then(@"the world has no live cells")]
    public void ThenEmpty()
    {
        Assert.Empty(_engine.Current.LiveCells);
    }

    [Then(@"creating a world with rule ""(.*)"" is rejected")]
    public void ThenRuleRejected(string rule)
    {
        Assert.Throws<FormatException>(() => Rule.Parse(rule));
    }

    private static Cell[] HorizontalBlinker(ulong cx, ulong cy) =>
    [
        new(unchecked(cx - 1), cy),
        new(cx, cy),
        new(unchecked(cx + 1), cy),
    ];

    private static Cell[] Block(ulong x, ulong y) =>
    [
        new(x, y), new(unchecked(x + 1), y),
        new(x, unchecked(y + 1)), new(unchecked(x + 1), unchecked(y + 1)),
    ];

    private static IEnumerable<Cell> GliderAt(Cell origin, ulong dx, ulong dy) =>
        GliderOffsets.Select(o => new Cell(
            unchecked(origin.X + (ulong)o.dx + dx),
            unchecked(origin.Y + (ulong)o.dy + dy)));
}
