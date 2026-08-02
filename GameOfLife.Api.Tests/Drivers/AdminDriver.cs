using System.Net.Http.Json;
using GameOfLife.Api.Features.CreateGame;
using GameOfLife.Api.Features.GameControl;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests.Drivers;

/// <summary>
/// The admin's side of the single-game vertical: creates and owns the game, holds the admin secret,
/// and issues secret-gated control verbs. Acts through the shared <see cref="ApiTestContext"/> world
/// and remembers the last responses so steps can assert on them. Reqnroll context-injects this
/// (and the same world instance) per scenario; it holds no assertions and no framework dependency.
/// </summary>
public sealed class AdminDriver(ApiTestContext ctx)
{
    /// <summary>The game this admin created (carrying the admin secret), or null before the first create.</summary>
    public CreateGameResponse? Game { get; private set; }

    /// <summary>The response to the most recent control verb, or null if none issued yet.</summary>
    public HttpResponseMessage? LastControl { get; private set; }

    /// <summary>The response to a rival's create attempt, or null if none was made.</summary>
    public HttpResponseMessage? RivalCreate { get; private set; }

    /// <summary>The admin secret for the owned game.</summary>
    public string AdminSecret => Game!.AdminSecret;

    /// <summary>Creates and takes ownership of the single game (asserting success at the world boundary).</summary>
    public async Task<CreateGameResponse> CreateGame(string? body = null)
    {
        Game = await ctx.CreateGame(body);
        return Game;
    }

    /// <summary>A second would-be admin races for the single slot; the raw response is kept for assertions.</summary>
    public async Task AnotherClientTriesToCreate()
    {
        RivalCreate = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));
    }

    public Task Start() => Control("start");
    public Task Pause() => Control("pause");
    public Task Resume() => Control("resume");
    public Task Stop() => Control("stop");
    public Task Step() => Control("step");

    /// <summary>Deserializes the last control response body (throws if none / non-OK unparseable).</summary>
    public Task<ControlResponse?> ReadLastControl() =>
        LastControl!.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json);

    private async Task Control(string verb)
    {
        LastControl = await ctx.Control(verb, AdminSecret);
    }
}
