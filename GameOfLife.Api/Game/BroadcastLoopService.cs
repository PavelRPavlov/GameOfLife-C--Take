namespace GameOfLife.Api.Game;

/// <summary>
/// The delta-broadcast seam the <see cref="BroadcastLoopService"/> drives, abstracted from the concrete
/// <see cref="GameHost"/> so a failing broadcast can be exercised in isolation without standing up the
/// full hub-backed host. The pump <see cref="WaitForPending"/>s until the simulation advances, then
/// <see cref="BroadcastPending"/>s the net delta — the two halves of one event-driven cadence.
/// </summary>
internal interface IBroadcaster
{
    /// <summary>
    /// Completes when the simulation has advanced since the last broadcast (a generation is pending), so
    /// the loop wakes on real progress rather than polling a fixed clock. Coalescing: any number of
    /// advances before the wake collapse into a single pending signal, which the following
    /// <see cref="BroadcastPending"/> resolves as one net delta spanning every skipped generation.
    /// </summary>
    Task WaitForPending(CancellationToken cancellationToken);

    /// <summary>Pushes a coalesced net delta since the last broadcast, if the game has advanced.</summary>
    Task BroadcastPending();
}

/// <summary>
/// Drives delivery from the simulation itself: it <see cref="IBroadcaster.WaitForPending"/>s until a
/// generation is produced, then pushes the net delta — one delta per generation, so the broadcast rate
/// simply <em>is</em> the tick rate, with no separate cadence to configure or keep in sync. The sim
/// never blocks on delivery, so a delivery hiccup cannot distort the simulation clock; it only causes
/// the advances that pile up during a slow send to coalesce into the next net delta once the pump
/// catches up. A single failing broadcast is logged and skipped — one bad tick must never tear down
/// server-wide broadcasting (nor, via the default <c>StopHost</c> behaviour, the whole process).
/// </summary>
internal sealed class BroadcastLoopService(
    IBroadcaster broadcaster,
    ILogger<BroadcastLoopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Block until the simulation advances — no wakeups while a game is idle or paused.
                await broadcaster.WaitForPending(stoppingToken);

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
