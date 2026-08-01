namespace GameOfLife.Client;

/// <summary>
/// The app-lifetime client aggregate and single source of truth. It owns one
/// <see cref="IGameStream"/> + <see cref="IGameApi"/> + <see cref="IAdminSecretStore"/> and drives
/// the whole attach protocol:
///
/// <list type="number">
///   <item>subscribe-first — connect the stream and start buffering deltas before fetching state;</item>
///   <item><c>GET /snapshot</c> — bootstrap the live set at generation <c>B</c>;</item>
///   <item>reconcile at <c>B</c> — adopt the snapshot, discard buffered deltas with <c>ToGen ≤ B</c>;</item>
///   <item>apply — drain the remaining buffered deltas, then apply each new one live;</item>
///   <item>resync rule — if the next delta cannot chain onto the current generation, re-fetch the snapshot.</item>
/// </list>
///
/// It exposes <em>granular</em> plain C# events so a status flip does not repaint the canvas and a
/// cell delta does not re-render the home page. Being framework-free, its reconcile/resync logic is
/// fully unit-testable against fake seams (the payoff of the seam).
///
/// Single-threaded consumer (Blazor Wasm): the lock guards only buffer/flag integrity against the
/// interleaving of an in-flight snapshot fetch with incoming stream pushes; events are always raised
/// outside the lock.
/// </summary>
public sealed class GameStore : IAsyncDisposable
{
    private readonly IGameApi _api;
    private readonly IGameStream _stream;
    private readonly IAdminSecretStore _secretStore;

    private readonly HashSet<Cell> _liveCells = new();
    private readonly List<Delta> _buffer = new();
    private readonly object _gate = new();

    // True while a snapshot fetch (attach or resync) is in flight: deltas are buffered, not applied.
    private bool _bootstrapping;
    // True once a snapshot baseline exists: deltas mutate the live set. Before this, deltas are dropped.
    private bool _observing;
    private bool _connected;

    public GameStore(IGameApi api, IGameStream stream, IAdminSecretStore secretStore)
    {
        _api = api;
        _stream = stream;
        _secretStore = secretStore;
        _stream.DeltaReceived += OnDeltaReceived;
        _stream.StatusReceived += OnStatusReceived;
    }

    /// <summary>The live cell set at the current <see cref="Generation"/>.</summary>
    public IReadOnlyCollection<Cell> LiveCells => _liveCells;

    /// <summary>The generation the live set currently reflects.</summary>
    public long Generation { get; private set; }

    /// <summary>The last known lifecycle status.</summary>
    public GameStatus Status { get; private set; } = GameStatus.NoGame;

    /// <summary>Whether an admin secret is held — the sole signal for admin-vs-observer affordances.</summary>
    public bool HasAdminSecret => _secretStore.HasSecret;

    /// <summary>Raised when <see cref="Status"/> changes (never for a no-op repeat).</summary>
    public event Action<GameStatus>? StatusChanged;

    /// <summary>Raised when <see cref="Generation"/> changes.</summary>
    public event Action<long>? GenerationChanged;

    /// <summary>Raised when the whole live set is (re)established from a snapshot — repaint fully.</summary>
    public event Action<Snapshot>? SnapshotApplied;

    /// <summary>Raised when a delta is applied to the live set — repaint incrementally.</summary>
    public event Action<Delta>? DeltaApplied;

    /// <summary>
    /// Connect the stream for reactive status only, without adopting a cell baseline (the home page's
    /// pre-observe path). Deltas that arrive before <see cref="AttachAsync"/> are dropped; status pushes
    /// update <see cref="Status"/> immediately.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;
        await _stream.ConnectAsync(ct);
        _connected = true;
    }

    /// <summary>
    /// Run the attach protocol and start observing the live cell field (the observer page's path).
    /// Buffering is armed <em>before</em> the connection/snapshot so no delta racing the fetch is lost.
    /// Returns the bootstrap snapshot, or the failure (e.g. <see cref="GameError.NoGame"/>).
    /// </summary>
    public async Task<Result<Snapshot, GameError>> AttachAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _bootstrapping = true;
            _buffer.Clear();
        }

        if (!_connected)
        {
            await _stream.ConnectAsync(ct);
            _connected = true;
        }

        var result = await _api.GetSnapshotAsync(ct);
        result.Match(ReconcileToSnapshot, OnBootstrapError);
        return result;
    }

    /// <summary>Create a game and, on success, persist the returned admin secret.</summary>
    public async Task<Result<CreatedGame, GameError>> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default)
    {
        var result = await _api.CreateGameAsync(request, ct);
        if (result.IsSuccess)
        {
            var game = result.Value;
            await _secretStore.SetAsync(game.Secret);
            SetStatus(game.Status);
            SetGeneration(game.Generation);
        }
        return result;
    }

    public Task<Result<ControlOutcome, GameError>> StartAsync(CancellationToken ct = default) => ControlAsync(_api.StartAsync, ct);
    public Task<Result<ControlOutcome, GameError>> StopAsync(CancellationToken ct = default) => ControlAsync(_api.StopAsync, ct);
    public Task<Result<ControlOutcome, GameError>> PauseAsync(CancellationToken ct = default) => ControlAsync(_api.PauseAsync, ct);
    public Task<Result<ControlOutcome, GameError>> ResumeAsync(CancellationToken ct = default) => ControlAsync(_api.ResumeAsync, ct);
    public Task<Result<ControlOutcome, GameError>> StepAsync(CancellationToken ct = default) => ControlAsync(_api.StepAsync, ct);

    private async Task<Result<ControlOutcome, GameError>> ControlAsync(
        Func<CancellationToken, Task<Result<ControlOutcome, GameError>>> op, CancellationToken ct)
    {
        var result = await op(ct);
        if (result.IsSuccess)
        {
            SetStatus(result.Value.Status);
            SetGeneration(result.Value.Generation);
        }
        else if (result.Error is GameError.Forbidden)
        {
            // The stored secret is stale — a client that can't authorise is an observer.
            await _secretStore.ClearAsync();
        }
        return result;
    }

    private void OnStatusReceived(GameStatus status) => SetStatus(status);

    private void OnDeltaReceived(Delta delta)
    {
        bool apply = false;
        lock (_gate)
        {
            if (_bootstrapping)
            {
                // A snapshot fetch is in flight: buffer for the drain that follows reconcile.
                _buffer.Add(delta);
                return;
            }

            if (!_observing) return;                 // no baseline yet — nothing to apply onto
            if (delta.ToGen <= Generation) return;   // duplicate or out-of-order stale delta — discard

            if (delta.FromGen == Generation)
            {
                apply = true;                        // chains cleanly onto the current generation
            }
            else
            {
                // Gap: the next delta cannot chain onto the current generation. Trip the resync rule.
                _bootstrapping = true;
                _buffer.Add(delta);
            }
        }

        if (apply)
        {
            Apply(delta);
            return;
        }

        _ = ResyncAsync();
    }

    private async Task ResyncAsync()
    {
        var result = await _api.GetSnapshotAsync();
        result.Match(ReconcileToSnapshot, OnBootstrapError);
    }

    private void ReconcileToSnapshot(Snapshot snapshot)
    {
        List<Delta> pending;
        lock (_gate)
        {
            _liveCells.Clear();
            foreach (var cell in snapshot.Cells) _liveCells.Add(cell);
            Generation = snapshot.Gen;

            pending = new List<Delta>(_buffer);
            _buffer.Clear();
            _bootstrapping = false;
            _observing = true;
        }

        SetStatus(snapshot.Status);
        SnapshotApplied?.Invoke(snapshot);
        GenerationChanged?.Invoke(Generation);

        // Replay whatever buffered while the snapshot was in flight, through the same rules:
        // ToGen ≤ B is discarded, the delta at B is applied, a still-present gap resyncs again.
        foreach (var delta in pending)
            OnDeltaReceived(delta);
    }

    private void OnBootstrapError(GameError error)
    {
        lock (_gate)
        {
            _bootstrapping = false;
            _observing = false;
            _buffer.Clear();
        }

        if (error is GameError.NoGame)
            SetStatus(GameStatus.NoGame);
    }

    private void Apply(Delta delta)
    {
        lock (_gate)
        {
            foreach (var cell in delta.Deaths) _liveCells.Remove(cell);
            foreach (var cell in delta.Births) _liveCells.Add(cell);
            Generation = delta.ToGen;
        }

        DeltaApplied?.Invoke(delta);
        GenerationChanged?.Invoke(delta.ToGen);
    }

    private void SetStatus(GameStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private void SetGeneration(long generation)
    {
        if (Generation == generation) return;
        Generation = generation;
        GenerationChanged?.Invoke(generation);
    }

    public async ValueTask DisposeAsync()
    {
        _stream.DeltaReceived -= OnDeltaReceived;
        _stream.StatusReceived -= OnStatusReceived;
        await _stream.DisposeAsync();
    }
}
