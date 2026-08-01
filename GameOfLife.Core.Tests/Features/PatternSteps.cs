using GameOfLife.Core.Tests.Drivers;
using GameOfLife.Core.Tests.Support;
using Reqnroll;

namespace GameOfLife.Core.Tests.Features;

/// <summary>
/// Thin Gherkin glue for the pattern-evolution vertical. All seeding, advancing, and scenario state
/// live on the context-injected <see cref="WorldDriver"/>; expected shapes come from
/// <see cref="Patterns"/>. Steps only translate Gherkin to driver calls and assert.
/// </summary>
[Binding]
public sealed class PatternSteps(WorldDriver world)
{
    [Given(@"a ""(.*)"" world seeded with a horizontal blinker centred at \((\d+), (\d+)\)")]
    public void GivenHorizontalBlinker(string rule, ulong cx, ulong cy) =>
        world.SeedHorizontalBlinker(rule, cx, cy);

    [Given(@"a ""(.*)"" world seeded with a glider at the torus origin corner")]
    public void GivenGliderAtCorner(string rule) => world.SeedGliderAtCorner(rule);

    [Given(@"a ""(.*)"" world seeded with a 2x2 block at \((\d+), (\d+)\)")]
    public void GivenBlock(string rule, ulong x, ulong y) => world.SeedBlock(rule, x, y);

    [Given(@"a ""(.*)"" world seeded with no live cells")]
    public void GivenEmpty(string rule) => world.CreateWorld(rule);

    [When(@"the world advances (\d+) generation(?:s)?")]
    public void WhenAdvance(int generations) => world.Advance(generations);

    [Then(@"the live cells are a horizontal blinker centred at \((\d+), (\d+)\)")]
    public void ThenHorizontalBlinker(ulong cx, ulong cy) =>
        Assert.Equal(Patterns.HorizontalBlinker(cx, cy).ToHashSet(), world.LiveCells);

    [Then(@"the live cells are a vertical blinker centred at \((\d+), (\d+)\)")]
    public void ThenVerticalBlinker(ulong cx, ulong cy) =>
        Assert.Equal(Patterns.VerticalBlinker(cx, cy).ToHashSet(), world.LiveCells);

    [Then(@"the glider has translated by \((\d+), (\d+)\) with wraparound")]
    public void ThenGliderTranslated(ulong dx, ulong dy) =>
        Assert.Equal(Patterns.GliderAt(world.GliderOrigin, dx, dy).ToHashSet(), world.LiveCells);

    [Then(@"the live cells are exactly the 2x2 block at \((\d+), (\d+)\)")]
    public void ThenBlock(ulong x, ulong y) =>
        Assert.Equal(Patterns.Block(x, y).ToHashSet(), world.LiveCells);

    [Then(@"the world has no live cells")]
    public void ThenEmpty() => Assert.Empty(world.LiveCells);

    [Then(@"creating a world with rule ""(.*)"" is rejected")]
    public void ThenRuleRejected(string rule) =>
        Assert.Throws<FormatException>(() => world.CreateWorld(rule));
}
