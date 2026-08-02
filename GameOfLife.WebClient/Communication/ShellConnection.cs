namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The app shell's connection phase — what <c>MainLayout</c> renders and every page gates on.
/// Distinct from the <em>game</em> status (<see cref="GameStore.Status"/>): this is about reaching
/// the server, not about whether a game exists.
/// </summary>
public enum ShellPhase
{
    /// <summary>The initial connect + first status fetch is in flight; views show "Connecting…".</summary>
    Connecting,

    /// <summary>Connected and the current status is known — pages render their game-state gate.</summary>
    Ready,

    /// <summary>The transport dropped and SignalR is auto-reconnecting; last-known status stays on screen.</summary>
    Reconnecting,

    /// <summary>The connection is down and won't retry on its own — the shell offers a manual Retry.</summary>
    Disconnected,
}

/// <summary>
/// The app-shell connection state machine, factored out of <c>MainLayout</c> so it is unit-testable
/// off the Wasm host (the same seam payoff the rest of <c>Communication</c> buys). It drives the one
/// app-lifetime connection through <see cref="GameStore"/>:
///
/// <list type="bullet">
///   <item><b>Connecting → Ready</b> — first render calls <see cref="Initialize"/>: connect the
///     stream, then one lightweight <see cref="GameStore.RefreshStatus"/> so views re-gate from
///     truth (a game may already be <c>Running</c>; SignalR is push-only and would never tell us).
///     <see cref="GameError.NoGame"/> is a <em>resolved</em> status, so it lands on <c>Ready</c>.</item>
///   <item><b>Ready → Reconnecting → Ready</b> — SignalR's built-in auto-reconnect fires
///     <see cref="StreamConnectionState.Reconnecting"/> then <see cref="StreamConnectionState.Reconnected"/>;
///     on recovery the status is re-fetched so a game that appeared/vanished while away is caught.</item>
///   <item><b>→ Disconnected</b> — the initial connect fails, the status fetch hits a
///     <see cref="GameError.Transport"/> failure, or the finite retry policy is exhausted
///     (<see cref="StreamConnectionState.Closed"/>). <see cref="Retry"/> re-runs the whole path.</item>
/// </list>
///
/// It holds no cell/game state — that stays in <see cref="GameStore"/>; it only owns the phase and
/// raises <see cref="Changed"/> so the shell can re-render. App-lifetime singleton, single-threaded
/// consumer (Blazor Wasm), so no locking.
/// </summary>
public sealed class ShellConnection : IDisposable
{
    private readonly GameStore _store;

    public ShellConnection(GameStore store)
    {
        _store = store;
        _store.ConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>The current shell phase.</summary>
    public ShellPhase Phase { get; private set; } = ShellPhase.Connecting;

    /// <summary>Raised whenever <see cref="Phase"/> changes, so the shell can re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// The eager startup path, run once on the shell's first render (and again by <see cref="Retry"/>):
    /// connect the stream, then seed status from the current server state. Reaching <c>Ready</c> means the
    /// status is known — including "no game exists"; only an unreachable server lands on <c>Disconnected</c>.
    /// </summary>
    public async Task Initialize(CancellationToken ct = default)
    {
        SetPhase(ShellPhase.Connecting);
        try
        {
            await _store.Connect(ct);
        }
        catch
        {
            // The transport could not be opened at all — nothing to reconnect yet, offer manual Retry.
            SetPhase(ShellPhase.Disconnected);
            return;
        }

        await Refresh(ct);
    }

    /// <summary>Manual retry from the <c>Disconnected</c> state — re-runs the full connect + status path.</summary>
    public Task Retry(CancellationToken ct = default) => Initialize(ct);

    private async Task Refresh(CancellationToken ct)
    {
        var result = await _store.RefreshStatus(ct);

        // Ok and NoGame both mean "status is known" → Ready. Only a Transport failure can't reach the
        // server and drops to Disconnected (the caller keeps whatever last-known status it had).
        var reachable = result.IsSuccess || result.Error is GameError.NoGame;
        SetPhase(reachable ? ShellPhase.Ready : ShellPhase.Disconnected);
    }

    private void OnConnectionStateChanged(StreamConnectionState state)
    {
        switch (state)
        {
            case StreamConnectionState.Reconnecting:
                SetPhase(ShellPhase.Reconnecting);
                break;

            case StreamConnectionState.Reconnected:
                // Back up — re-gate from truth (a game may have been created/transitioned/vanished while away).
                _ = Refresh(CancellationToken.None);
                break;

            case StreamConnectionState.Closed:
                SetPhase(ShellPhase.Disconnected);
                break;
        }
    }

    private void SetPhase(ShellPhase phase)
    {
        if (Phase == phase) return;
        Phase = phase;
        Changed?.Invoke();
    }

    public void Dispose() => _store.ConnectionStateChanged -= OnConnectionStateChanged;
}
