namespace GameOfLife.Api.Game;

/// <summary>
/// The coalesced-broadcast trigger the <see cref="BroadcastLoopService"/> pulses on its interval,
/// abstracted from the concrete <see cref="GameHost"/> so a failing broadcast can be exercised in
/// isolation without standing up the full hub-backed host.
/// </summary>
internal interface IBroadcaster
{
    /// <summary>Pushes a coalesced net delta since the last broadcast, if the game has advanced.</summary>
    Task BroadcastPending();
}

/// <summary>
/// Drives the server-wide coalesced broadcast cadence: every <see cref="GameOptions.BroadcastIntervalMs"/>
/// it asks the host to push a net delta if the game has advanced. Independent of the simulation
/// clock, so delivery hiccups never distort the simulation. A single failing broadcast is logged and
/// skipped — one bad tick must never tear down server-wide broadcasting (nor, via the default
/// <c>StopHost</c> behaviour, the whole process).
/// </summary>
internal sealed class BroadcastLoopService(
    IBroadcaster broadcaster,
    IOptions<GameOptions> options,
    ILogger<BroadcastLoopService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(options.Value.BroadcastIntervalMs);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await broadcaster.BroadcastPending();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw; // Shutdown — let the outer handler end the loop rather than swallowing it.
                }
                catch (Exception ex)
                {
                    // A single bad broadcast must not stop the server-wide cadence: log and keep looping.
                    logger.LogError(ex, "A broadcast tick failed; continuing the broadcast loop.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
