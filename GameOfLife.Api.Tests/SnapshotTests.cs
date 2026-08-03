using System.Net;
using System.Net.Http.Json;
using GameOfLife.Core;
using GameOfLife.Api.Features.GetSnapshot;
using GameOfLife.Api.Tests.Support;
using GameOfLife.Shared;

namespace GameOfLife.Api.Tests;

/// <summary>The view-only <c>GET /snapshot</c> bootstrap: no secret, full live set at a known generation.</summary>
public class SnapshotTests
{
    [Fact]
    public async Task Given_no_game_exists_When_a_snapshot_is_requested_Then_the_result_is_404_game_not_found()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.GetSnapshot();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.GameNotFound);
        Assert.Empty(error.Errors);
    }

    [Fact]
    public async Task Given_a_newly_created_game_When_a_snapshot_is_requested_Then_it_reports_created_at_generation_0()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGame(); // autoStart false → held Created

        var snapshot = await (await ctx.GetSnapshot()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);

        Assert.Equal(GameStatus.Created, snapshot!.Status);
        Assert.Equal(0, snapshot.Gen);
        Assert.Equal(100, snapshot.TickRate);
    }

    [Fact]
    public async Task Given_a_game_seeded_with_cells_at_an_origin_When_a_snapshot_is_requested_Then_it_reflects_the_seed_cells_placed_at_the_origin()
    {
        await using var ctx = new ApiTestContext();
        // Blinker centred at grid (row 10, col 10), placed at origin (1000, 2000).
        var seed = TestSeeds.HorizontalBlinker(row: 10, col: 10);
        await ctx.CreateGame(Requests.ValidCreate(seed: seed, originX: "1000", originY: "2000"));

        var snapshot = await (await ctx.GetSnapshot()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);

        // (row, col) → (x = origin.x + col, y = origin.y + row).
        var expected = new HashSet<(string, string)>
        {
            ("1009", "2010"),
            ("1010", "2010"),
            ("1011", "2010"),
        };
        var actual = snapshot!.Cells.Select(c => (c.X, c.Y)).ToHashSet();
        Assert.Equal(expected, actual);
    }
}
