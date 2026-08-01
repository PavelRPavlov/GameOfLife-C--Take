using GameOfLife.Core;

namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The stateless REST seam. Control verbs take <em>no</em> secret parameter — the real
/// implementation attaches the <c>X-Admin-Secret</c> header transparently from
/// <see cref="IAdminSecretStore"/>. Absence of a game is the value <see cref="GameError.NoGame"/>,
/// never a null.
/// </summary>
public interface IGameApi
{
    Task<Result<CreatedGame, GameError>> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> StartAsync(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> StopAsync(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> PauseAsync(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> ResumeAsync(CancellationToken ct = default);
    Task<Result<ControlOutcome, GameError>> StepAsync(CancellationToken ct = default);
    Task<Result<Snapshot, GameError>> GetSnapshotAsync(CancellationToken ct = default);
}

/// <summary>
/// A thin transport over the push-only SignalR hub: connect, then surface the two <em>raw</em>
/// server pushes. It holds no domain state and does no reconcile — that is <see cref="GameStore"/>'s job.
/// </summary>
public interface IGameStream : IAsyncDisposable
{
    /// <summary>Raw <c>ReceiveDelta</c> push.</summary>
    event Action<Delta> DeltaReceived;

    /// <summary>Raw <c>ReceiveStatus</c> push (fired on every backend lifecycle transition).</summary>
    event Action<GameStatus> StatusReceived;

    Task ConnectAsync(CancellationToken ct = default);
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

    Task SetAsync(string secret);
    Task ClearAsync();

    /// <summary>Raised whenever the stored secret is set or cleared.</summary>
    event Action? Changed;
}
