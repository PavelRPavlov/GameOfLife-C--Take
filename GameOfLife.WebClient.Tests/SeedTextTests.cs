using GameOfLife.WebClient.Communication.Seeding;

namespace GameOfLife.WebClient.Tests;

public class SeedTextTests
{
    private static readonly HashSet<(int, int)> Glider = [(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)];

    [Fact]
    public void Rle_DecodesAGlider()
    {
        var result = SeedText.Parse("bo$2bo$3o!");

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Rle_HonoursRunCounts()
    {
        var result = SeedText.Parse("3o!");
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (1, 0), (2, 0) }, result.Cells.ToHashSet());
    }

    [Fact]
    public void Rle_IgnoresHeaderAndCommentLines()
    {
        var rle = "#N Glider\nx = 3, y = 3, rule = B3/S23\nbo$2bo$3o!";
        var result = SeedText.Parse(rle);

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Rle_StopsAtBang()
    {
        var result = SeedText.Parse("o!oooo");
        Assert.Equal(new HashSet<(int, int)> { (0, 0) }, result.Cells.ToHashSet());
    }

    [Fact]
    public void Plaintext_DecodesAGlider()
    {
        var result = SeedText.Parse(".O.\n..O\nOOO");

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Plaintext_SkipsBangComments()
    {
        var result = SeedText.Parse("!Name: Glider\n.O.\n..O\nOOO");
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Rle_FlagsClampedWhenCellsFallOutside()
    {
        // Advance to column 100 (off-board), then place a live cell there.
        var result = SeedText.Parse("100bo!");

        Assert.True(result.Clamped);
        Assert.Empty(result.Cells);
    }

    [Fact]
    public void Plaintext_ClampsRowsBeyondTheBoard()
    {
        // 101 rows: the last live cell is on row 100 (off-board).
        var rows = string.Join('\n', Enumerable.Repeat("O", 101));
        var result = SeedText.Parse(rows);

        Assert.True(result.Clamped);
        Assert.Equal(SeedBoard.Size, result.Cells.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Blank_YieldsEmpty(string? text)
    {
        var result = SeedText.Parse(text);

        Assert.Empty(result.Cells);
        Assert.False(result.Clamped);
    }
}
