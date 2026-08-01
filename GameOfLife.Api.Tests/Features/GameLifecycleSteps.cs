using System.Net;
using System.Net.Http.Json;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Tests.Support;
using Reqnroll;

namespace GameOfLife.Api.Tests.Features;

[Binding]
public sealed class GameLifecycleSteps
{
    private readonly ApiTestContext _ctx = new();
    private ObserverClient? _observer;
    private CreateGameResponse? _game;
    private HttpResponseMessage? _lastControl;
    private HttpResponseMessage? _secondCreate;

    [Given(@"an observer is connected")]
    public async Task GivenObserverConnected()
    {
        _observer = await _ctx.ConnectObserverAsync();
    }

    [When(@"the admin creates a game")]
    public async Task WhenAdminCreatesGame()
    {
        _game = await _ctx.CreateGameAsync();
    }

    [When(@"another client tries to create a game")]
    public async Task WhenAnotherClientCreates()
    {
        _secondCreate = await _ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));
    }

    [When(@"the admin starts the game")]
    public async Task WhenAdminStarts() => _lastControl = await _ctx.ControlAsync("start", _game!.AdminSecret);

    [When(@"the admin pauses the game")]
    public async Task WhenAdminPauses() => _lastControl = await _ctx.ControlAsync("pause", _game!.AdminSecret);

    [When(@"the admin stops the game")]
    public async Task WhenAdminStops() => _lastControl = await _ctx.ControlAsync("stop", _game!.AdminSecret);

    [Then(@"the create response carries an admin secret")]
    public void ThenCreateCarriesSecret()
    {
        Assert.True(Guid.TryParse(_game!.AdminSecret, out _));
    }

    [Then(@"the control response status is ""(.*)""")]
    public async Task ThenControlStatus(string expected)
    {
        Assert.Equal(HttpStatusCode.OK, _lastControl!.StatusCode);
        var body = await _lastControl.Content.ReadFromJsonAsync<ControlResponse>(ApiTestContext.Json);
        Assert.Equal(Enum.Parse<GameStatus>(expected), body!.Status);
    }

    [Then(@"the observer is told the status is ""(.*)""")]
    public async Task ThenObserverToldStatus(string expected)
    {
        var status = Enum.Parse<GameStatus>(expected);
        var seen = await _observer!.WaitForAsync(o => o.Statuses.Contains(status));
        Assert.True(seen, $"observer never saw {status}; saw [{string.Join(", ", _observer.Statuses)}]");
    }

    [Then(@"creating a new game issues a different admin secret")]
    public async Task ThenNewGameDifferentSecret()
    {
        var next = await _ctx.CreateGameAsync();
        Assert.NotEqual(_game!.AdminSecret, next.AdminSecret);
    }

    [Then(@"the second create attempt is refused as a conflict")]
    public void ThenSecondCreateConflict()
    {
        Assert.Equal(HttpStatusCode.Conflict, _secondCreate!.StatusCode);
    }

    [AfterScenario]
    public async Task Cleanup() => await _ctx.DisposeAsync();
}
