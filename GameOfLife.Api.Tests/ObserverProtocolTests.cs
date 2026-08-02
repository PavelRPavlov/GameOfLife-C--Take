using System.Net.Http.Json;
using GameOfLife.Core;
using GameOfLife.Api.Features.GameControl;
using GameOfLife.Api.Features.GetSnapshot;
using GameOfLife.Api.Game;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The observer protocol as a client sees it over real SignalR: a status push on every lifecycle
/// transition, small births/deaths deltas on the hot path, immediate broadcast on step, and
/// subscribe-first reconciliation that reconstructs the live set exactly.
/// </summary>
public class ObserverProtocolTests
{
    private static string BlinkerAutoStart() =>
        Requests.ValidCreate(seed: TestSeeds.HorizontalBlinker(50, 50), autoStart: true);

    [Fact]
    public async Task Given_an_observer_connected_before_any_game_When_the_game_cycles_through_every_lifecycle_transition_Then_a_status_is_pushed_for_each()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserver();

        var game = await ctx.CreateGame(); // NoGame → Created
        await ctx.Control("start", game.AdminSecret);   // → Running
        await ctx.Control("pause", game.AdminSecret);   // → Paused
        await ctx.Control("resume", game.AdminSecret);  // → Running
        await ctx.Control("stop", game.AdminSecret);    // → NoGame

        var sawAll = await observer.WaitFor(o =>
            o.Statuses.Count >= 5);
        Assert.True(sawAll, $"Only saw statuses: [{string.Join(", ", observer.Statuses)}]");

        Assert.Equal(
            [GameStatus.Created, GameStatus.Running, GameStatus.Paused, GameStatus.Running, GameStatus.NoGame],
            observer.Statuses);
    }

    [Fact]
    public async Task Given_an_observer_watching_a_created_game_When_the_game_is_stopped_Then_NoGame_is_pushed()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserver();
        var game = await ctx.CreateGame();

        await ctx.Control("stop", game.AdminSecret);

        Assert.True(await observer.WaitFor(o => o.Statuses.Contains(GameStatus.NoGame)));
    }

    [Fact]
    public async Task Given_an_observer_watching_an_autostarted_running_game_When_the_world_ticks_Then_births_and_deaths_deltas_are_pushed()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserver();

        await ctx.CreateGame(BlinkerAutoStart());

        var gotDelta = await observer.WaitFor(o =>
            o.Deltas.Any(d => d.Births.Count > 0 && d.Deaths.Count > 0));
        Assert.True(gotDelta, "expected at least one non-empty births/deaths delta");

        var delta = observer.Deltas.First(d => d.Births.Count > 0);
        Assert.True(delta.ToGen > delta.FromGen);
    }

    [Fact]
    public async Task Given_an_observer_watching_a_paused_game_When_a_single_step_is_issued_Then_the_resulting_delta_is_broadcast_immediately()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame(BlinkerAutoStart());
        // Pause so stepping is legal and the world is quiescent.
        await ctx.Control("pause", game.AdminSecret);

        var observer = await ctx.ConnectObserver();

        var stepResponse = await ctx.Control("step", game.AdminSecret);
        var control = await stepResponse.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json);

        var sawStepDelta = await observer.WaitFor(o => o.Deltas.Any(d => d.ToGen == control!.Generation));
        Assert.True(sawStepDelta,
            $"expected an immediate delta with ToGen={control!.Generation}; " +
            $"saw deltas: [{string.Join(", ", observer.Deltas.Select(d => $"{d.FromGen}->{d.ToGen}"))}]");
    }

    [Fact]
    public async Task Given_a_subscribe_first_attach_to_a_running_game_When_a_snapshot_is_taken_Then_the_next_delta_chains_from_it_with_no_resync()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGame(BlinkerAutoStart()); // running, ticking

        // Subscribe-first against a RUNNING game, then snapshot at generation B.
        var observer = await ctx.ConnectObserver();
        var snapshot = await (await ctx.GetSnapshot()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);
        var baseGen = snapshot!.Gen;

        // Wait for the next delta that advances past B.
        await observer.WaitFor(o => o.Deltas.Any(d => d.ToGen > baseGen));

        // The first applicable delta must chain from B exactly — no straddle, no resync.
        var firstAfter = observer.Deltas.Where(d => d.ToGen > baseGen).OrderBy(d => d.FromGen).First();
        Assert.Equal(baseGen, firstAfter.FromGen);
    }

    [Fact]
    public async Task Given_a_subscribe_first_attach_and_a_snapshot_baseline_When_deltas_are_applied_in_order_Then_the_reconstructed_live_set_matches_a_fresh_snapshot()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame(BlinkerAutoStart());
        // Freeze the world so the reconciliation is deterministic.
        await ctx.Control("pause", game.AdminSecret);

        // Subscribe-first: connect and buffer, THEN fetch the snapshot at generation B.
        var observer = await ctx.ConnectObserver();
        var snapshot = await (await ctx.GetSnapshot()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);
        var baseGen = snapshot!.Gen;

        // Advance deterministically via single-steps; each pushes one delta.
        for (var i = 0; i < 3; i++)
            await ctx.Control("step", game.AdminSecret);

        // Receive the three step deltas.
        await observer.WaitFor(o => o.Deltas.Count(d => d.FromGen >= baseGen) >= 3);

        // Reconstruct: start from the snapshot, discard deltas at or before B, apply the rest in order.
        var live = snapshot.Cells.Select(c => (c.X, c.Y)).ToHashSet();
        var applicable = observer.Deltas.Where(d => d.ToGen > baseGen).OrderBy(d => d.FromGen).ToList();

        var expectedFrom = baseGen;
        foreach (var delta in applicable)
        {
            Assert.Equal(expectedFrom, delta.FromGen); // no gap — deltas chain
            foreach (var death in delta.Deaths) live.Remove((death.X, death.Y));
            foreach (var birth in delta.Births) live.Add((birth.X, birth.Y));
            expectedFrom = delta.ToGen;
        }

        // The reconstructed set must equal a fresh snapshot at the final generation.
        var finalSnapshot = await (await ctx.GetSnapshot()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);
        var expected = finalSnapshot!.Cells.Select(c => (c.X, c.Y)).ToHashSet();
        Assert.Equal(expected, live);
        Assert.Equal(finalSnapshot.Gen, expectedFrom);
    }
}
