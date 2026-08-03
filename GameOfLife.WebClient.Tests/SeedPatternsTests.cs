using GameOfLife.WebClient.Communication.Seeding;

namespace GameOfLife.WebClient.Tests;

public class SeedPatternsTests
{
    [Fact]
    public void Given_the_seed_pattern_library_When_listing_all_patterns_Then_it_contains_the_resolved_names_in_order()
    {
        var names = SeedPatterns.All.Select(p => p.Name).ToArray();
        Assert.Equal(
            ["Gosper gun x 2", "Glider", "LWSS", "Pulsar", "Gosper gun"],
            names);
    }

    [Theory]
    [InlineData("Gosper gun x 2", 72, 80, 9)]
    [InlineData("Glider", 5, 3, 3)]
    [InlineData("LWSS", 9, 5, 4)]
    [InlineData("Pulsar", 48, 13, 13)]
    [InlineData("Gosper gun", 36, 36, 9)]
    public void Given_a_named_pattern_When_inspecting_its_geometry_Then_the_cell_count_and_bounding_box_match(string name, int cells, int width, int height)
    {
        var pattern = SeedPatterns.All.Single(p => p.Name == name);

        Assert.Equal(cells, pattern.Cells.Count);
        Assert.Equal(width, pattern.Width);
        Assert.Equal(height, pattern.Height);
    }

    [Fact]
    public void Given_every_seed_pattern_When_placed_Then_it_fits_within_the_board()
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
