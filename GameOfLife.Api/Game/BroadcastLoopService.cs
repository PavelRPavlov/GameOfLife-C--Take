namespace GameOfLife.Api.Game;

/// <summary>
/// Drives the server-wide coalesced broadcast cadence: every <see cref="GameOptions.BroadcastIntervalMs"/>
/// it asks the host to push a net delta if the game has advanced. Independent of the simulation
/// clock, so delivery hiccups never distort the simulation.
/// </summary>
public sealed class BroadcastLoopService(GameHost host, IOptions<GameOptions> options) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(options.Value.BroadcastIntervalMs);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
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
