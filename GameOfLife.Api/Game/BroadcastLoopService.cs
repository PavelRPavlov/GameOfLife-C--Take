namespace GameOfLife.Api.Game;

/// <summary>
/// Drives the server-wide coalesced broadcast cadence: every <see cref="GameHost.BroadcastInterval"/>
/// it asks the host to push a net delta if the game has advanced. Independent of the simulation
/// clock, so delivery hiccups never distort the simulation.
/// </summary>
public sealed class BroadcastLoopService(GameHost host) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(GameHost.BroadcastInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await host.BroadcastPendingAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
