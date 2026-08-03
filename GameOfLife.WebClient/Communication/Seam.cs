namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The stateless REST seam. Control verbs take <em>no</em> secret parameter — the real
/// implementation attaches the <c>X-Admin-Secret</c> header transparently from
/// <see cref="IAdminSecretStore"/>. Absence of a game is the value <see cref="GameError.NoGame"/>,
/// never a null.
/// </summary>
public interface IGameApi
{
    Task<Result<CreatedGame, GameError>> CreateGame(CreateGameRequest request, CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> Start(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> Stop(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> Pause(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> Resume(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> Step(CancellationToken ct = default);
    Task<Result<Snapshot, GameError>> GetSnapshot(CancellationToken ct = default);
}

/// <summary>
/// The transport's connection lifecycle, mapped from the underlying <c>HubConnection</c>. Surfaced so
/// the app shell can drive its Connecting… → Connected → Reconnecting… → Disconnected/Retry state
/// machine: auto-reconnect fires <see cref="Reconnecting"/> then <see cref="Reconnected"/>; when the
/// finite retry policy is exhausted the connection fires <see cref="Closed"/> (the manual-Retry state).
/// </summary>
public enum StreamConnectionState
{
    /// <summary>Auto-reconnect has started; the connection is temporarily down.</summary>
    Reconnecting,

    /// <summary>Auto-reconnect succeeded; pushes will resume (an observer re-syncs via the gap rule).</summary>
    Reconnected,

    /// <summary>The connection is closed and will not retry on its own — the shell falls back to manual Retry.</summary>
    Closed,
}

/// <summary>
/// A thin transport over the push-only SignalR hub: connect, then surface the two <em>raw</em>
/// server pushes plus the connection lifecycle. It holds no domain state and does no reconcile — that
/// is <see cref="GameStore"/>'s job.
/// </summary>
public interface IGameStream : IAsyncDisposable
{
    /// <summary>Raw <c>ReceiveDelta</c> push.</summary>
    event Action<Delta> DeltaReceived;

    /// <summary>Raw <c>ReceiveStatus</c> push (fired on every backend lifecycle transition).</summary>
    event Action<GameStatus> StatusReceived;

    /// <summary>The transport's connection lifecycle (auto-reconnect and final close).</summary>
    event Action<StreamConnectionState> ConnectionStateChanged;

    Task Connect(CancellationToken ct = default);
}

/// <summary>
/// Ambient persistence of the admin capability (localStorage-backed in the real impl). The raw
/// GUID is read only by the API implementation to set the <c>X-Admin-Secret</c> header; component
/// code decides admin-vs-observer affordances from <see cref="HasSecret"/> alone.
/// </summary>
public interface IAdminSecretStore
{
    bool HasSecret { get; }

    /// <summary>The stored secret, or null. Intended for the API implementation only.</summary>
    string? Current { get; }

    Task Set(string secret);
    Task Clear();
}
