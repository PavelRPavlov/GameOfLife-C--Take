using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>
/// Combinatorial truth-table and <see cref="ulong"/>-boundary cases for the engine, asserting
/// observed generation-to-generation behaviour and the delta a caller reads back — never the
/// private set internals.
/// </summary>
public class GameEngineTheoryTests
{
    // A dead cell centred in a field of exactly `neighbours` live cells: born iff count == 3
    // under B3/S23. We surround the origin with the first `neighbours` of its 8 Moore neighbours.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    public void Birth_of_a_dead_cell_follows_B3(int neighbours, bool expectedBorn)
    {
        var center = new Cell(1000, 1000);
        var seed = FirstNeighbours(center, neighbours);
        var engine = new GameEngine(seed, Rule.Parse("B3/S23"));

        var next = engine.Advance();

        Assert.Equal(expectedBorn, next.LiveCells.Contains(center));
        Assert.Equal(expectedBorn, next.Births.Contains(center));
    }

    // A live cell with `neighbours` live neighbours: survives iff count is 2 or 3 under B3/S23.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    public void Survival_of_a_live_cell_follows_S23(int neighbours, bool expectedSurvives)
    {
        var center = new Cell(2000, 2000);
        var seed = new List<Cell> { center };
        seed.AddRange(FirstNeighbours(center, neighbours));
        var engine = new GameEngine(seed, Rule.Parse("B3/S23"));

        var next = engine.Advance();

        Assert.Equal(expectedSurvives, next.LiveCells.Contains(center));
        // A cell that fails to survive appears in Deaths; one that survives does not.
        Assert.Equal(!expectedSurvives, next.Deaths.Contains(center));
    }

    [Fact]
    public void A_block_still_life_is_unchanged_and_reports_no_delta()
    {
        // 2x2 block is stable under B3/S23.
        var block = new[]
        {
            new Cell(5, 5), new Cell(6, 5),
            new Cell(5, 6), new Cell(6, 6),
        };
        var engine = new GameEngine(block, Rule.Parse("B3/S23"));

        var next = engine.Advance();

        Assert.Equal(block.ToHashSet(), next.LiveCells);
        Assert.Empty(next.Births);
        Assert.Empty(next.Deaths);
    }

    [Fact]
    public void A_blinker_straddling_the_x_seam_wraps_correctly()
    {
        // Horizontal blinker centred on x == 0, so it straddles the 2^64 seam:
        // cells at x = MaxValue, 0, 1 (all y = 10).
        var rule = Rule.Parse("B3/S23");
        var seed = new[]
        {
            new Cell(ulong.MaxValue, 10),
            new Cell(0, 10),
            new Cell(1, 10),
        };
        var engine = new GameEngine(seed, rule);

        var next = engine.Advance();

        // A period-2 blinker rotates to vertical: same centre column x = 0, y = 9,10,11.
        var expected = new HashSet<Cell>
        {
            new(0, 9),
            new(0, 10),
            new(0, 11),
        };
        Assert.Equal(expected, next.LiveCells);
    }

    [Fact]
    public void A_block_straddling_the_corner_where_both_axes_wrap_is_stable()
    {
        // 2x2 block spanning (MaxValue,MaxValue) — wraps on BOTH axes to (0,0).
        var m = ulong.MaxValue;
        var block = new[]
        {
            new Cell(m, m), new Cell(0, m),
            new Cell(m, 0), new Cell(0, 0),
        };
        var engine = new GameEngine(block, Rule.Parse("B3/S23"));

        var next = engine.Advance();

        Assert.Equal(block.ToHashSet(), next.LiveCells);
    }

    [Fact]
    public void An_all_dead_world_stays_empty()
    {
        var engine = new GameEngine([], Rule.Parse("B3/S23"));

        var next = engine.Advance();

        Assert.Empty(next.LiveCells);
        Assert.Empty(next.Births);
        Assert.Empty(next.Deaths);
        Assert.Equal(1, next.Number);
    }

    /// <summary>Returns the first <paramref name="count"/> of the 8 Moore neighbours of a cell.</summary>
    private static List<Cell> FirstNeighbours(Cell center, int count)
    {
        var offsets = new (int dx, int dy)[]
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0), (1, 0),
            (-1, 1), (0, 1), (1, 1),
        };
        var result = new List<Cell>(count);
        for (var i = 0; i < count; i++)
            result.Add(new Cell(
                unchecked(center.X + (ulong)offsets[i].dx),
                unchecked(center.Y + (ulong)offsets[i].dy)));
        return result;
    }
}
