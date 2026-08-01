using System.Net;
using System.Net.Http.Json;
using GameOfLife.Core;
using GameOfLife.Api.Features.GameControl;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The control-verb matrix: the existence → auth → state check order, constant-time secret gating,
/// and strict rejection of no-op transitions. Errors are bodyless 404/403/409.
/// </summary>
public class ControlVerbTests
{
    private static readonly string[] AllVerbs = ["start", "stop", "pause", "resume", "step"];

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("step")]
    public async Task Verb_against_no_game_is_404_even_with_a_secret(string verb)
    {
        await using var ctx = new ApiTestContext();

        // Existence is checked before auth: a syntactically valid secret still gets 404 when empty.
        var response = await ctx.ControlAsync(verb, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("step")]
    public async Task Verb_with_bad_or_missing_secret_is_403_when_a_game_exists(string verb)
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGameAsync();

        var missing = await ctx.ControlAsync(verb, secret: null);
        var wrong = await ctx.ControlAsync(verb, Guid.NewGuid().ToString());
        var garbage = await ctx.ControlAsync(verb, "not-a-guid");

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, garbage.StatusCode);
    }

    [Fact]
    public async Task Existence_is_checked_before_auth()
    {
        await using var ctx = new ApiTestContext();

        // No game yet — even a wrong secret must yield 404 (existence), not 403 (auth).
        var response = await ctx.ControlAsync("start", "not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_from_Created_runs_the_game()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync();

        var response = await ctx.ControlAsync("start", game.AdminSecret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json);
        Assert.Equal(GameStatus.Running, body!.Status);
    }

    [Fact]
    public async Task Starting_an_already_running_game_is_409()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync(Requests.ValidCreate(autoStart: true));

        var response = await ctx.ControlAsync("start", game.AdminSecret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resuming_a_game_that_never_ran_is_409()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync(); // held Created

        var response = await ctx.ControlAsync("resume", game.AdminSecret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Stepping_a_running_game_is_409()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync(Requests.ValidCreate(autoStart: true));

        var response = await ctx.ControlAsync("step", game.AdminSecret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Pause_then_resume_round_trips()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGameAsync(Requests.ValidCreate(autoStart: true));

        var paused = await ctx.ControlAsync("pause", game.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.Equal(GameStatus.Paused, (await paused.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json))!.Status);

        var resumed = await ctx.ControlAsync("resume", game.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(GameStatus.Running, (await resumed.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json))!.Status);
    }

    [Fact]
    public async Task Stop_frees_the_slot_and_the_next_create_gets_a_new_secret()
    {
        await using var ctx = new ApiTestContext();
        var first = await ctx.CreateGameAsync();

        var stop = await ctx.ControlAsync("stop", first.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal(GameStatus.NoGame, (await stop.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json))!.Status);

        // Slot freed — a fresh create succeeds and issues a different secret.
        var second = await ctx.CreateGameAsync();
        Assert.NotEqual(first.AdminSecret, second.AdminSecret);

        // The old secret no longer controls the new game.
        var stale = await ctx.ControlAsync("start", first.AdminSecret);
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
    }

    [Fact]
    public async Task Auth_is_checked_before_state()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGameAsync(); // Created — a start would be a legal state transition

        // Wrong secret on an otherwise-legal transition must fail auth (403), not proceed.
        var response = await ctx.ControlAsync("start", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
