using GameOfLife.Core;
using GameOfLife.Api.Game;
using GameOfLife.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace GameOfLife.Api.Tests;

/// <summary>
/// Process shutdown must stop a running game, not leave its simulation loop ticking until the process
/// dies. The host's shutdown seam is the DI container disposing the singleton <see cref="GameHost"/>;
/// this drives that seam directly against the real wired host and asserts the running game is torn down
/// the same way a Stop verb tears it down — the terminal NoGame is pushed to observers.
/// </summary>
public class GracefulShutdownTests
{
    [Fact]
    public async Task Given_a_running_game_When_the_host_is_disposed_on_shutdown_Then_the_session_is_stopped_and_NoGame_is_pushed()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserver();

        // A running game (autoStart) so the sim loop is actually ticking when shutdown fires.
        await ctx.CreateGame(Requests.ValidCreate(autoStart: true));
        Assert.True(await observer.WaitFor(o => o.Statuses.Contains(GameStatus.Running)),
            "the game never reached Running before shutdown");

        // Drive the shutdown seam the app runs at process exit: disposing the singleton host. This
        // reuses the same in-gate stop path as the Stop verb, so observers see the terminal NoGame.
        var host = ctx.Services.GetRequiredService<GameHost>();
        await host.DisposeAsync();

        Assert.True(await observer.WaitFor(o => o.Statuses.Contains(GameStatus.NoGame)),
            $"shutdown did not stop the running game; saw statuses: [{string.Join(", ", observer.Statuses)}]");

        // Disposal is idempotent: the container captures the same instance twice (GameHost + IBroadcaster)
        // and disposes it again at context teardown. A second dispose must be a safe no-op, not throw.
        await host.DisposeAsync();
    }
}
