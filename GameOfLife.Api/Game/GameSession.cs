using System.Buffers.Text;

namespace GameOfLife.Api.Game;

/// <summary>
/// One live game: the engine, its ownership secret, and the simulation tick loop. The loop is the
/// single writer of the engine; <see cref="Step"/> only ever runs while the loop is stopped, so
/// the single-writer invariant holds. Legal-transition checks live in <see cref="GameHost"/>; this
/// type just performs the mechanics.
/// </summary>
internal sealed class GameSession
{
    private readonly ISimulationEngine _engine;
    private readonly TimeSpan _period;
    private readonly ILogger<GameSession> _logger;
    private readonly Func<GameSession, Task> _onFaulted;
    private readonly Action _onAdvanced;

    private CancellationTokenSource? _loopCts;
    private Task _loopTask = Task.CompletedTask;

    public GameSession(
        IReadOnlyCollection<Cell> seed,
        Rule rule,
        double tickRate,
        bool autoStart,
        Universe universe,
        ILogger<GameSession> logger,
        Func<GameSession, Task> onFaulted,
        Action onAdvanced)
        : this(new GameEngineSimulation(new GameEngine(seed, rule, universe)), rule, tickRate, autoStart, logger, onFaulted, onAdvanced)
    {
    }

    /// <summary>
    /// The engine seam for tests: builds a session over an arbitrary <see cref="ISimulationEngine"/> so a
    /// tick that throws can be driven through the loop. Production flows through the public constructor,
    /// which wraps the concrete <see cref="GameEngine"/>.
    /// </summary>
    internal GameSession(
        ISimulationEngine engine,
        Rule rule,
        double tickRate,
        bool autoStart,
        ILogger<GameSession> logger,
        Func<GameSession, Task> onFaulted,
        Action onAdvanced)
    {
        _engine = engine;
        Rule = rule;
        TickRate = tickRate;
        _period = TimeSpan.FromSeconds(1.0 / tickRate);
        _logger = logger;
        _onFaulted = onFaulted;
        _onAdvanced = onAdvanced;
        // A 256-bit token from the CSPRNG, base64url-encoded. Full random unpredictability for a value
        // that is the sole bearer credential for game control (a time-ordered GUID would leak a timestamp).
        AdminSecret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        Status = GameStatus.Created;

        if (autoStart)
            StartLoop(GameStatus.Running);
    }

    /// <summary>The one-time ownership secret — a 256-bit CSPRNG token (base64url), compared in constant time by the host.</summary>
    public string AdminSecret { get; }

    public Rule Rule { get; }

    public double TickRate { get; }

    public GameStatus Status { get; private set; }

    /// <summary>The current generation. Safe to read from any thread (observer snapshots, broadcaster).</summary>
    public Generation Current => _engine.Current;

    /// <summary>Created → Running: begin ticking.</summary>
    public void Start() => StartLoop(GameStatus.Running);

    /// <summary>Paused → Running: continue ticking from where it froze.</summary>
    public void Resume() => StartLoop(GameStatus.Running);

    /// <summary>Running → Paused: freeze the loop, awaiting its shutdown so no further tick races a step.</summary>
    public async Task Pause()
    {
        await StopLoop();
        Status = GameStatus.Paused;
    }

    /// <summary>Advance exactly one generation while paused.</summary>
    public Generation Step() => _engine.Advance();

    /// <summary>Any state → torn down: stop the loop and await its shutdown.</summary>
    public Task Stop() => StopLoop();

    private void StartLoop(GameStatus runningStatus)
    {
        Status = runningStatus;
        _loopCts = new CancellationTokenSource();
        _loopTask = Run(_loopCts.Token);
    }

    private async Task Run(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_period);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                _engine.Advance();
                // Pulse the broadcaster so the new generation is delivered; non-blocking, so delivery
                // never paces the simulation. Must not throw (the host's signal swallows contention).
                _onAdvanced();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on pause/stop.
        }
        catch (Exception ex)
        {
            // A tick faulted. Left unhandled the loop task would fault silently: the game would freeze
            // at its last generation while Status still read Running, with no log and no recovery. Instead
            // log, mark this session terminal (NoGame — the enum has no Faulted/Stopped state and this is
            // exactly what the host is about to push), and hand off to the host to free the slot and tell
            // observers.
            _logger.LogError(ex, "Simulation loop for game (rule {Rule}) faulted; stopping the game.", Rule);
            Status = GameStatus.NoGame;

            // Fire-and-forget: StopLoop awaits this very task under the host's state gate, and the handoff
            // below takes that same gate — awaiting it here would deadlock a concurrent stop/pause.
            // Detaching lets the loop task complete so the gate is free for the handoff to acquire.
            _ = NotifyHostFaulted();
        }
    }

    private async Task NotifyHostFaulted()
    {
        try
        {
            await _onFaulted(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hand a faulted game loop off to the host.");
        }
    }

    private async Task StopLoop()
    {
        if (_loopCts is null) return;

        await _loopCts.CancelAsync();
        try
        {
            await _loopTask;
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = Task.CompletedTask;
    }
}

/// <summary>
/// The sim loop's single-writer engine seam: the tick (<see cref="Advance"/>) plus the currently
/// published generation. Abstracted so a fault in the tick can be exercised in isolation with a test
/// engine that throws, without standing up the concrete <see cref="GameEngine"/>.
/// </summary>
internal interface ISimulationEngine
{
    /// <summary>The most recently published generation. Safe to read from any thread.</summary>
    Generation Current { get; }

    /// <summary>Computes and publishes the next generation, returning it. Called by a single writer.</summary>
    Generation Advance();
}

/// <summary>Adapts the concrete sparse <see cref="GameEngine"/> to <see cref="ISimulationEngine"/>.</summary>
internal sealed class GameEngineSimulation(GameEngine engine) : ISimulationEngine
{
    public Generation Current => engine.Current;

    public Generation Advance() => engine.Advance();
}
