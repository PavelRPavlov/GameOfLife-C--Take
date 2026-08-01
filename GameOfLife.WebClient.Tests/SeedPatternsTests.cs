using GameOfLife.WebClient.Communication.Seeding;

namespace GameOfLife.WebClient.Tests;

public class SeedPatternsTests
{
    [Fact]
    public void All_ContainsTheResolvedLibrary()
    {
        var names = SeedPatterns.All.Select(p => p.Name).ToArray();
        Assert.Equal(
            ["Block", "Blinker", "Glider", "LWSS", "Pulsar", "Gosper gun"],
            names);
    }

    [Theory]
    [InlineData("Block", 4, 2, 2)]
    [InlineData("Blinker", 3, 3, 1)]
    [InlineData("Glider", 5, 3, 3)]
    [InlineData("LWSS", 9, 5, 4)]
    [InlineData("Pulsar", 48, 13, 13)]
    [InlineData("Gosper gun", 36, 36, 9)]
    public void Pattern_HasExpectedCellCountAndBoundingBox(string name, int cells, int width, int height)
    {
        var pattern = SeedPatterns.All.Single(p => p.Name == name);

        Assert.Equal(cells, pattern.Cells.Count);
        Assert.Equal(width, pattern.Width);
        Assert.Equal(height, pattern.Height);
    }

    [Fact]
    public void EveryPattern_FitsWithinTheBoard()
    {
        foreach (var pattern in SeedPatterns.All)
        {
            Assert.True(pattern.Width <= SeedBoard.Size);
            Assert.True(pattern.Height <= SeedBoard.Size);
            Assert.All(pattern.Cells, c =>
            {
                Assert.InRange(c.X, 0, SeedBoard.Size - 1);
                Assert.InRange(c.Y, 0, SeedBoard.Size - 1);
            });
        }
    }
}
