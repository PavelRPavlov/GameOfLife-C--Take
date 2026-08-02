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
    public async Task Snapshot_with_no_game_maps_to_GAME_NOT_FOUND()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.GetSnapshotAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.GameNotFound);
        Assert.Empty(error.Errors);
    }

    [Fact]
    public async Task A_created_game_reports_Created_at_generation_0()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGameAsync(); // autoStart false → held Created

        var snapshot = await (await ctx.GetSnapshotAsync()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);

        Assert.Equal(GameStatus.Created, snapshot!.Status);
        Assert.Equal(0, snapshot.Gen);
        Assert.Equal(10, snapshot.TickRate);
    }

    [Fact]
    public async Task Snapshot_reflects_the_seed_cells_placed_at_the_origin()
    {
        await using var ctx = new ApiTestContext();
        // Blinker centred at grid (row 10, col 10), placed at origin (1000, 2000).
        var seed = TestSeeds.HorizontalBlinker(row: 10, col: 10);
        await ctx.CreateGameAsync(Requests.ValidCreate(seed: seed, originX: "1000", originY: "2000"));

        var snapshot = await (await ctx.GetSnapshotAsync()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);

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
