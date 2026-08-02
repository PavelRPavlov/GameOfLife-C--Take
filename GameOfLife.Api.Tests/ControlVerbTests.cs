using System.Net;
using System.Net.Http.Json;
using GameOfLife.Core;
using GameOfLife.Api.Features.GameControl;
using GameOfLife.Api.Tests.Support;
using GameOfLife.Shared;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The control-verb matrix: the existence → auth → state check order, constant-time secret gating,
/// and strict rejection of no-op transitions. Errors carry the uniform envelope with a machine-readable
/// code (404 GAME_NOT_FOUND / 403 INVALID_ADMIN_SECRET / 409 INVALID_STATE_FOR_VERB).
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
    public async Task Given_no_game_exists_When_a_control_verb_is_issued_with_a_valid_secret_Then_the_result_is_404_game_not_found(string verb)
    {
        await using var ctx = new ApiTestContext();

        // Existence is checked before auth: a syntactically valid secret still gets 404 when empty.
        var response = await ctx.Control(verb, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await response.ReadError(ErrorCodes.GameNotFound);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("step")]
    public async Task Given_a_game_exists_When_a_control_verb_is_issued_with_a_bad_or_missing_secret_Then_the_result_is_403_invalid_admin_secret(string verb)
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGame();

        var missing = await ctx.Control(verb, secret: null);
        var wrong = await ctx.Control(verb, Guid.NewGuid().ToString());
        var garbage = await ctx.Control(verb, "not-a-guid");

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, garbage.StatusCode);
        await missing.ReadError(ErrorCodes.InvalidAdminSecret);
        await wrong.ReadError(ErrorCodes.InvalidAdminSecret);
        await garbage.ReadError(ErrorCodes.InvalidAdminSecret);
    }

    [Fact]
    public async Task Given_no_game_exists_When_a_verb_is_issued_with_a_wrong_secret_Then_existence_is_checked_before_auth_yielding_404()
    {
        await using var ctx = new ApiTestContext();

        // No game yet — even a wrong secret must yield 404 (existence), not 403 (auth).
        var response = await ctx.Control("start", "not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await response.ReadError(ErrorCodes.GameNotFound);
    }

    [Fact]
    public async Task Given_a_created_game_When_start_is_issued_Then_the_game_runs()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame();

        var response = await ctx.Control("start", game.AdminSecret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json);
        Assert.Equal(GameStatus.Running, body!.Status);
    }

    [Fact]
    public async Task Given_an_already_running_game_When_start_is_issued_again_Then_the_result_is_409_invalid_state_naming_running()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame(Requests.ValidCreate(autoStart: true));

        var response = await ctx.Control("start", game.AdminSecret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.InvalidStateForVerb);
        // The message names the current state in friendly words (Running → "running").
        Assert.Contains("running", error.Message);
        Assert.Empty(error.Errors);
    }

    [Fact]
    public async Task Given_a_created_game_that_never_ran_When_resume_is_issued_Then_the_result_is_409_invalid_state_naming_the_waiting_state()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame(); // held Created

        var response = await ctx.Control("resume", game.AdminSecret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.InvalidStateForVerb);
        // Created → "waiting to start".
        Assert.Contains("waiting to start", error.Message);
    }

    [Fact]
    public async Task Given_a_running_game_When_step_is_issued_Then_the_result_is_409_invalid_state_naming_running()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame(Requests.ValidCreate(autoStart: true));

        var response = await ctx.Control("step", game.AdminSecret);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.InvalidStateForVerb);
        Assert.Contains("running", error.Message);
    }

    [Fact]
    public async Task Given_a_running_game_When_it_is_paused_then_resumed_Then_it_round_trips_back_to_running()
    {
        await using var ctx = new ApiTestContext();
        var game = await ctx.CreateGame(Requests.ValidCreate(autoStart: true));

        var paused = await ctx.Control("pause", game.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.Equal(GameStatus.Paused, (await paused.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json))!.Status);

        var resumed = await ctx.Control("resume", game.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(GameStatus.Running, (await resumed.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json))!.Status);
    }

    [Fact]
    public async Task Given_a_game_is_stopped_When_a_new_game_is_created_Then_the_slot_is_freed_and_a_new_secret_is_issued()
    {
        await using var ctx = new ApiTestContext();
        var first = await ctx.CreateGame();

        var stop = await ctx.Control("stop", first.AdminSecret);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal(GameStatus.NoGame, (await stop.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json))!.Status);

        // Slot freed — a fresh create succeeds and issues a different secret.
        var second = await ctx.CreateGame();
        Assert.NotEqual(first.AdminSecret, second.AdminSecret);

        // The old secret no longer controls the new game.
        var stale = await ctx.Control("start", first.AdminSecret);
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        await stale.ReadError(ErrorCodes.InvalidAdminSecret);
    }

    [Fact]
    public async Task Given_a_created_game_and_an_otherwise_legal_transition_When_the_verb_is_issued_with_a_wrong_secret_Then_auth_is_checked_before_state_yielding_403()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGame(); // Created — a start would be a legal state transition

        // Wrong secret on an otherwise-legal transition must fail auth (403), not proceed.
        var response = await ctx.Control("start", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await response.ReadError(ErrorCodes.InvalidAdminSecret);
    }
}
