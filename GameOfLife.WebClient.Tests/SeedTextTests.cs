using GameOfLife.WebClient.Communication.Seeding;

namespace GameOfLife.WebClient.Tests;

public class SeedTextTests
{
    private static readonly HashSet<(int, int)> Glider = [(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)];

    [Fact]
    public void Given_RLE_text_for_a_glider_When_parsed_Then_it_decodes_the_glider()
    {
        var result = SeedText.Parse("bo$2bo$3o!");

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_RLE_text_with_run_counts_When_parsed_Then_the_run_counts_are_honoured()
    {
        var result = SeedText.Parse("3o!");
        Assert.Equal(new HashSet<(int, int)> { (0, 0), (1, 0), (2, 0) }, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_RLE_text_with_header_and_comment_lines_When_parsed_Then_those_lines_are_ignored()
    {
        var rle = "#N Glider\nx = 3, y = 3, rule = B3/S23\nbo$2bo$3o!";
        var result = SeedText.Parse(rle);

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_RLE_text_with_content_after_the_bang_When_parsed_Then_parsing_stops_at_the_bang()
    {
        var result = SeedText.Parse("o!oooo");
        Assert.Equal(new HashSet<(int, int)> { (0, 0) }, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_RLE_text_without_a_terminator_When_parsed_Then_it_parses_through_to_the_end()
    {
        // No trailing '!': the parser must still return the cells accumulated up to the end of input.
        var result = SeedText.Parse("bo$2bo$3o");

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_plaintext_for_a_glider_When_parsed_Then_it_decodes_the_glider()
    {
        var result = SeedText.Parse(".O.\n..O\nOOO");

        Assert.False(result.Clamped);
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_plaintext_with_bang_comments_When_parsed_Then_the_comments_are_skipped()
    {
        var result = SeedText.Parse("!Name: Glider\n.O.\n..O\nOOO");
        Assert.Equal(Glider, result.Cells.ToHashSet());
    }

    [Fact]
    public void Given_RLE_text_placing_a_cell_off_board_When_parsed_Then_it_flags_clamped_and_drops_the_cell()
    {
        // Advance to column 100 (off-board), then place a live cell there.
        var result = SeedText.Parse("100bo!");

        Assert.True(result.Clamped);
        Assert.Empty(result.Cells);
    }

    [Fact]
    public void Given_plaintext_with_rows_beyond_the_board_When_parsed_Then_it_clamps_the_rows()
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
    public void Given_blank_text_When_parsed_Then_it_yields_an_empty_unclamped_result(string? text)
    {
        var result = SeedText.Parse(text);

        Assert.Empty(result.Cells);
        Assert.False(result.Clamped);
    }
}
