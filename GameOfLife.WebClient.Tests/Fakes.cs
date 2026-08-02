using GameOfLife.Core;
using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// A hand-driven <see cref="IGameApi"/>. Snapshot results are served in order (the last one repeats),
/// with an optional gate so a test can inject deltas <em>while</em> a fetch is in flight — the window
/// the attach protocol's buffering exists to cover.
/// </summary>
internal sealed class FakeGameApi : IGameApi
{
    private readonly List<Result<Snapshot, GameError>> _snapshots = new();
    private int _snapshotIndex;

    public Result<CreatedGame, GameError> CreateResult { get; set; } =
        Result<CreatedGame, GameError>.Err(new GameError.InvalidState("invalid state"));

    public Result<ControlOutcome, GameError> ControlResult { get; set; } =
        Result<ControlOutcome, GameError>.Ok(new ControlOutcome(GameStatus.Running, 0));

    public int SnapshotCalls { get; private set; }

    /// <summary>When set, <see cref="GetSnapshotAsync"/> awaits this before returning its result.</summary>
    public TaskCompletionSource? SnapshotGate { get; set; }

    public void EnqueueSnapshot(Result<Snapshot, GameError> result) => _snapshots.Add(result);

    public async Task<Result<CreatedGame, GameError>> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return CreateResult;
    }

    public Task<Result<ControlOutcome, GameError>> StartAsync(CancellationToken ct = default) => Task.FromResult(ControlResult);
    public Task<Result<ControlOutcome, GameError>> StopAsync(CancellationToken ct = default) => Task.FromResult(ControlResult);
    public Task<Result<ControlOutcome, GameError>> PauseAsync(CancellationToken ct = default) => Task.FromResult(ControlResult);
    public Task<Result<ControlOutcome, GameError>> ResumeAsync(CancellationToken ct = default) => Task.FromResult(ControlResult);
    public Task<Result<ControlOutcome, GameError>> StepAsync(CancellationToken ct = default) => Task.FromResult(ControlResult);

    public Task<Result<Snapshot, GameError>> GetSnapshotAsync(CancellationToken ct = default)
    {
        SnapshotCalls++;
        var index = Math.Min(_snapshotIndex, _snapshots.Count - 1);
        _snapshotIndex++;
        var result = _snapshots[index];

        return SnapshotGate is null
            ? Task.FromResult(result)
            : SnapshotGate.Task.ContinueWith(_ => result, TaskScheduler.Default);
    }
}

/// <summary>A hand-driven <see cref="IGameStream"/> — a test raises the two raw pushes directly.</summary>
internal sealed class FakeGameStream : IGameStream
{
    public event Action<Delta>? DeltaReceived;
    public event Action<GameStatus>? StatusReceived;
    public event Action<StreamConnectionState>? ConnectionStateChanged;

    public bool Connected { get; private set; }

    /// <summary>When set, <see cref="ConnectAsync"/> faults with this instead of connecting.</summary>
    public Exception? ConnectException { get; set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (ConnectException is not null)
            return Task.FromException(ConnectException);
        Connected = true;
        return Task.CompletedTask;
    }

    public void PushDelta(Delta delta) => DeltaReceived?.Invoke(delta);
    public void PushStatus(GameStatus status) => StatusReceived?.Invoke(status);
    public void PushConnectionState(StreamConnectionState state) => ConnectionStateChanged?.Invoke(state);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>An in-memory <see cref="IAdminSecretStore"/>.</summary>
internal sealed class FakeAdminSecretStore : IAdminSecretStore
{
    public bool HasSecret => Current is not null;
    public string? Current { get; private set; }
    public event Action? Changed;

    public Task SetAsync(string secret)
    {
        Current = secret;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Current = null;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
