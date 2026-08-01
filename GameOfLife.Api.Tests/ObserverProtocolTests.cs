using System.Net.Http.Json;
using GameOfLife.Api.Contracts;
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
    public async Task Status_is_pushed_on_every_lifecycle_transition()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserverAsync();

        var game = await ctx.CreateGameAsync(); // NoGame → Created
        await ctx.ControlAsync("start", game.AdminSecret);   // → Running
        await ctx.ControlAsync("pause", game.AdminSecret);   // → Paused
        await ctx.ControlAsync("resume", game.AdminSecret);  // → Running
        await ctx.ControlAsync("stop", game.AdminSecret);    // → NoGame

        var sawAll = await observer.WaitForAsync(o =>
            o.Statuses.Count >= 5);
        Assert.True(sawAll, $"Only saw statuses: [{string.Join(", ", observer.Statuses)}]");

        Assert.Equal(
            [GameStatus.Created, GameStatus.Running, GameStatus.Paused, GameStatus.Running, GameStatus.NoGame],
            observer.Statuses);
    }

    [Fact]
    public async Task A_stopped_game_pushes_NoGame()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserverAsync();
        var game = await ctx.CreateGameAsync();

        await ctx.ControlAsync("stop", game.AdminSecret);

        Assert.True(await observer.WaitForAsync(o => o.Statuses.Contains(GameStatus.NoGame)));
    }

    [Fact]
    public async Task A_running_game_pushes_births_and_deaths_deltas()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserverAsync();

        await ctx.CreateGameAsync(BlinkerAutoStart());

        var gotDelta = await observer.WaitForAsync(o =>
            o.Deltas.Any(d => d.Births.Count > 0 && d.Deaths.Count > 0));
        Assert.True(gotDelta, "expected at least one non-empty births/deaths delta");

        var delta = observer.Deltas.First(d => d.Births.Count > 0);
        Assert.True(delta.ToGen > delta.FromGen);
    }

    [Fact]
    public async Task Single_step_broadcasts_immediately()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync(BlinkerAutoStart());
        // Pause so stepping is legal and the world is quiescent.
        await ctx.ControlAsync("pause", game.AdminSecret);

        var observer = await ctx.ConnectObserverAsync();

        var stepResponse = await ctx.ControlAsync("step", game.AdminSecret);
        var control = await stepResponse.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json);

        var sawStepDelta = await observer.WaitForAsync(o => o.Deltas.Any(d => d.ToGen == control!.Generation));
        Assert.True(sawStepDelta,
            $"expected an immediate delta with ToGen={control!.Generation}; " +
            $"saw deltas: [{string.Join(", ", observer.Deltas.Select(d => $"{d.FromGen}->{d.ToGen}"))}]");
    }

    [Fact]
    public async Task Snapshot_is_broadcast_aligned_so_a_running_attach_needs_no_immediate_resync()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGameAsync(BlinkerAutoStart()); // running, ticking

        // Subscribe-first against a RUNNING game, then snapshot at generation B.
        var observer = await ctx.ConnectObserverAsync();
        var snapshot = await (await ctx.GetSnapshotAsync()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);
        var baseGen = snapshot!.Gen;

        // Wait for the next delta that advances past B.
        await observer.WaitForAsync(o => o.Deltas.Any(d => d.ToGen > baseGen));

        // The first applicable delta must chain from B exactly — no straddle, no resync.
        var firstAfter = observer.Deltas.Where(d => d.ToGen > baseGen).OrderBy(d => d.FromGen).First();
        Assert.Equal(baseGen, firstAfter.FromGen);
    }

    [Fact]
    public async Task Subscribe_first_attach_reconciles_the_live_set_exactly()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync(BlinkerAutoStart());
        // Freeze the world so the reconciliation is deterministic.
        await ctx.ControlAsync("pause", game.AdminSecret);

        // Subscribe-first: connect and buffer, THEN fetch the snapshot at generation B.
        var observer = await ctx.ConnectObserverAsync();
        var snapshot = await (await ctx.GetSnapshotAsync()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);
        var baseGen = snapshot!.Gen;

        // Advance deterministically via single-steps; each pushes one delta.
        for (var i = 0; i < 3; i++)
            await ctx.ControlAsync("step", game.AdminSecret);

        // Receive the three step deltas.
        await observer.WaitForAsync(o => o.Deltas.Count(d => d.FromGen >= baseGen) >= 3);

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
        var finalSnapshot = await (await ctx.GetSnapshotAsync()).Content.ReadFromJsonAsync<SnapshotResponse>(ApiTestContext.Json);
        var expected = finalSnapshot!.Cells.Select(c => (c.X, c.Y)).ToHashSet();
        Assert.Equal(expected, live);
        Assert.Equal(finalSnapshot.Gen, expectedFrom);
    }
}
