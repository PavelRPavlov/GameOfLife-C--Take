using System.Net;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Tests.Drivers;
using Reqnroll;

namespace GameOfLife.Api.Tests.Features;

/// <summary>
/// Thin Gherkin glue for the single-game admin/observer vertical. All acting and state live on the
/// context-injected <see cref="AdminDriver"/>/<see cref="ObserverDriver"/> (which share one world);
/// the world's teardown lives in <see cref="Support.ScenarioHooks"/>. Steps only translate Gherkin
/// to driver calls and assert.
/// </summary>
[Binding]
public sealed class GameLifecycleSteps(AdminDriver admin, ObserverDriver observer)
{
    [Given(@"an observer is connected")]
    public Task GivenObserverConnected() => observer.Connect();

    [When(@"the admin creates a game")]
    public Task WhenAdminCreatesGame() => admin.CreateGame();

    [When(@"another client tries to create a game")]
    public Task WhenAnotherClientCreates() => admin.AnotherClientTriesToCreate();

    [When(@"the admin starts the game")]
    public Task WhenAdminStarts() => admin.Start();

    [When(@"the admin pauses the game")]
    public Task WhenAdminPauses() => admin.Pause();

    [When(@"the admin stops the game")]
    public Task WhenAdminStops() => admin.Stop();

    [Then(@"the create response carries an admin secret")]
    public void ThenCreateCarriesSecret()
    {
        Assert.True(Guid.TryParse(admin.AdminSecret, out _));
    }

    [Then(@"the control response status is ""(.*)""")]
    public async Task ThenControlStatus(string expected)
    {
        Assert.Equal(HttpStatusCode.OK, admin.LastControl!.StatusCode);
        var body = await admin.ReadLastControl();
        Assert.Equal(Enum.Parse<GameStatus>(expected), body!.Status);
    }

    [Then(@"the observer is told the status is ""(.*)""")]
    public async Task ThenObserverToldStatus(string expected)
    {
        var status = Enum.Parse<GameStatus>(expected);
        var seen = await observer.WaitForStatus(status);
        Assert.True(seen, $"observer never saw {status}; saw [{string.Join(", ", observer.Statuses)}]");
    }

    [Then(@"creating a new game issues a different admin secret")]
    public async Task ThenNewGameDifferentSecret()
    {
        var previous = admin.AdminSecret;
        var next = await admin.CreateGame();
        Assert.NotEqual(previous, next.AdminSecret);
    }

    [Then(@"the second create attempt is refused as a conflict")]
    public void ThenSecondCreateConflict()
    {
        Assert.Equal(HttpStatusCode.Conflict, admin.RivalCreate!.StatusCode);
    }
}
