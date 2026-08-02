namespace GameOfLife.Api.Game;

/// <summary>
/// The process-lifetime, in-memory owner of the single game. The slot is Empty ⇄ Occupied: a
/// <c>Stopped</c> game frees the slot (the next creator becomes the new admin with a fresh secret),
/// a <c>Paused</c> game keeps it occupied; there are never two games at once. Control transitions
/// are serialized through <see cref="_stateGate"/> and follow the existence → auth → state order.
/// The host also owns delta broadcasting: a coalesced net snapshot-diff over a server-wide interval,
/// plus an immediate broadcast on single-step, decoupled from the simulation clock.
/// </summary>
public sealed class GameHost
{
    /// <summary>Relative URL of the SignalR hub, handed to clients in the create response.</summary>
    public const string HubUrl = "/hubs/game";

    /// <summary>Relative URL of the snapshot endpoint, handed to clients in the create response.</summary>
    public const string SnapshotUrl = "/snapshot";

    private readonly IHubContext<GameHub, IGameClient> _hub;
    private readonly Universe _universe;

    // Serializes create + control transitions (state machine). The sim loop runs outside this gate.
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    // Serializes broadcast state between the interval loop and the immediate step broadcast.
    private readonly SemaphoreSlim _broadcastGate = new(1, 1);

    // Written under _stateGate; read lock-free by observers and the broadcaster, so volatile
    // (matching the care GameEngine takes with its current-generation reference).
    private volatile GameSession? _session;

    // Broadcast baseline: the session, generation, and live set as of the last push.
    // All broadcast-baseline fields are accessed only under _broadcastGate.
    private GameSession? _broadcastSession;
    private long _lastBroadcastGen = -1;
    private HashSet<Cell> _lastBroadcastLive = [];

    public GameHost(IHubContext<GameHub, IGameClient> hub, Universe universe)
    {
        _hub = hub;
        _universe = universe;
    }

    /// <summary>
    /// Atomically claims the empty slot with a new game, or refuses if one already exists.
    /// Returns the created session (with its one-time secret) on success, or null on conflict.
    /// </summary>
    internal async Task<GameSession?> TryCreate(GameParameters parameters)
    {
        await _stateGate.WaitAsync();
        try
        {
            if (_session is not null)
                return null; // 409 — exactly one game.

            var session = new GameSession(parameters.Seed, parameters.Rule, parameters.TickRate, parameters.AutoStart, _universe);
            _session = session;
            // Establish the broadcast baseline at gen 0 (the seed) up front, so the very first
            // delta — including an immediate broadcast from a single-step — is computed and pushed
            // rather than being swallowed as a lazily-initialized baseline.
            await SetBroadcastBaseline(session, parameters.Seed);
            await PushStatus(session.Status);
            return session;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    // Start / pause / resume / step each require a specific current state; stop is legal from any
    // existing state. The gate + existence→auth→state boilerplate lives once in RunControl;
    // each verb supplies only its required state and its transition body.

    public Task<ControlOutcome> Start(string? secret) =>
        RunControl(secret, requiredState: GameStatus.Created, async session =>
        {
            session.Start();
            await PushStatus(session.Status);
            return ControlOutcome.Ok(session.Status, session.Current.Number);
        });

    public Task<ControlOutcome> Pause(string? secret) =>
        RunControl(secret, requiredState: GameStatus.Running, async session =>
        {
            await session.Pause();
            await PushStatus(session.Status);
            return ControlOutcome.Ok(session.Status, session.Current.Number);
        });

    public Task<ControlOutcome> Resume(string? secret) =>
        RunControl(secret, requiredState: GameStatus.Paused, async session =>
        {
            session.Resume();
            await PushStatus(session.Status);
            return ControlOutcome.Ok(session.Status, session.Current.Number);
        });

    public Task<ControlOutcome> Step(string? secret) =>
        RunControl(secret, requiredState: GameStatus.Paused, async session =>
        {
            var generation = session.Step();
            // Single-step broadcasts immediately (not on the coalesced interval).
            await BroadcastPending();
            return ControlOutcome.Ok(session.Status, generation.Number);
        });

    public Task<ControlOutcome> Stop(string? secret) =>
        RunControl(secret, requiredState: null, async session =>
        {
            var finalGeneration = session.Current.Number;
            await session.Stop();
            _session = null; // Free the slot for the next first-starter.
            await PushStatus(GameStatus.NoGame);
            return ControlOutcome.Ok(GameStatus.NoGame, finalGeneration);
        });

    /// <summary>
    /// Runs a control verb under the state gate, enforcing the existence → auth → state order:
    /// slot empty → 404, bad/missing secret → 403, wrong state → 409 (no-ops rejected). On success
    /// the verb's <paramref name="action"/> performs the transition and produces the outcome.
    /// </summary>
    private async Task<ControlOutcome> RunControl(
        string? secret,
        GameStatus? requiredState,
        Func<GameSession, Task<ControlOutcome>> action)
    {
        await _stateGate.WaitAsync();
        try
        {
            if (!TryAuthorize(secret, out var session, out var error)) return error;
            if (requiredState is { } required && session.Status != required) return ControlOutcome.InvalidState(session.Status);
            return await action(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// The view-only snapshot at the last broadcast boundary (generation and live set), or null when
    /// the slot is empty. Aligning the snapshot to the broadcast baseline — not the latest simulation
    /// generation — is what makes subscribe-first reconciliation exact: the next delta an observer
    /// receives chains from this generation, so no delta straddles the snapshot and trips a resync.
    /// </summary>
    internal async Task<GameSnapshot?> GetSnapshot()
    {
        var session = _session;
        if (session is null)
            return null;

        await _broadcastGate.WaitAsync();
        try
        {
            var cells = _lastBroadcastLive.Select(c => c.ToDto()).ToList();
            return new GameSnapshot(_lastBroadcastGen, session.Status, session.TickRate, cells);
        }
        finally
        {
            _broadcastGate.Release();
        }
    }

    /// <summary>
    /// Pushes a coalesced net delta since the last broadcast, if the game has advanced. Called both
    /// on the server-wide interval and immediately after a single-step. Safe to call concurrently.
    /// </summary>
    public async Task BroadcastPending()
    {
        await _broadcastGate.WaitAsync();
        try
        {
            var session = _session;
            if (session is null)
            {
                ResetBaseline();
                return;
            }

            if (!ReferenceEquals(session, _broadcastSession))
            {
                // Safety net only: the baseline is normally set at creation. If a game somehow
                // appears un-baselined, baseline at its current generation (observers bootstrap via
                // GET /snapshot and the gen-0 signal, so no delta is synthesized for the baseline).
                var baseline = session.Current;
                _broadcastSession = session;
                _lastBroadcastGen = baseline.Number;
                _lastBroadcastLive = [.. baseline.LiveCells];
                return;
            }

            var generation = session.Current;
            if (generation.Number == _lastBroadcastGen)
                return; // Nothing new since the last broadcast.

            var births = generation.LiveCells.Where(c => !_lastBroadcastLive.Contains(c)).ToList();
            var deaths = _lastBroadcastLive.Where(c => !generation.LiveCells.Contains(c)).ToList();

            // Columnar (SoA): all X's, then all Y's — X[i] pairs with Y[i]. Coordinates stay native
            // ulong so values above 2^53 and the torus boundaries survive the wire exactly.
            var delta = new DeltaDto(
                _lastBroadcastGen, generation.Number,
                births.Select(c => c.X).ToArray(), births.Select(c => c.Y).ToArray(),
                deaths.Select(c => c.X).ToArray(), deaths.Select(c => c.Y).ToArray());
            await _hub.Clients.All.ReceiveDelta(delta);

            _lastBroadcastGen = generation.Number;
            _lastBroadcastLive = [.. generation.LiveCells];
        }
        finally
        {
            _broadcastGate.Release();
        }
    }

    private async Task SetBroadcastBaseline(GameSession session, IReadOnlyCollection<Cell> seed)
    {
        await _broadcastGate.WaitAsync();
        try
        {
            _broadcastSession = session;
            _lastBroadcastGen = 0; // The seed is generation 0.
            _lastBroadcastLive = [.. seed];
        }
        finally
        {
            _broadcastGate.Release();
        }
    }

    private void ResetBaseline()
    {
        _broadcastSession = null;
        _lastBroadcastGen = -1;
        _lastBroadcastLive = [];
    }

    private async Task PushStatus(GameStatus status) => await _hub.Clients.All.ReceiveStatus(status);

    /// <summary>
    /// Enforces the existence → auth order. On failure sets <paramref name="error"/> to the
    /// bodyless 404/403 outcome; on success yields the live <paramref name="session"/>.
    /// </summary>
    private bool TryAuthorize(string? secret, out GameSession session, out ControlOutcome error)
    {
        var current = _session;
        if (current is null)
        {
            session = null!;
            error = ControlOutcome.NoGame;
            return false;
        }

        if (!SecretMatches(current, secret))
        {
            session = null!;
            error = ControlOutcome.Forbidden;
            return false;
        }

        session = current;
        error = default;
        return true;
    }

    private static bool SecretMatches(GameSession session, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || !Guid.TryParse(secret, out var provided))
            return false;

        Span<byte> expected = stackalloc byte[16];
        Span<byte> actual = stackalloc byte[16];
        session.AdminSecret.TryWriteBytes(expected);
        provided.TryWriteBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
