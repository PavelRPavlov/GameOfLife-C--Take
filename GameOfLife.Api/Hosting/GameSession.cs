using GameOfLife.Api.Contracts;
using GameOfLife.Core;

namespace GameOfLife.Api.Hosting;

/// <summary>
/// One live game: the engine, its ownership secret, and the simulation tick loop. The loop is the
/// single writer of the engine; <see cref="Step"/> only ever runs while the loop is stopped, so
/// the single-writer invariant holds. Legal-transition checks live in <see cref="GameHost"/>; this
/// type just performs the mechanics.
/// </summary>
internal sealed class GameSession
{
    private readonly GameEngine _engine;
    private readonly TimeSpan _period;

    private CancellationTokenSource? _loopCts;
    private Task _loopTask = Task.CompletedTask;

    public GameSession(IReadOnlyCollection<Cell> seed, Rule rule, double tickRate, bool autoStart)
    {
        _engine = new GameEngine(seed, rule);
        Rule = rule;
        TickRate = tickRate;
        _period = TimeSpan.FromSeconds(1.0 / tickRate);
        AdminSecret = Guid.CreateVersion7();
        Status = GameStatus.Created;

        if (autoStart)
            StartLoop(GameStatus.Running);
    }

    /// <summary>The one-time ownership secret, compared in constant time by the host.</summary>
    public Guid AdminSecret { get; }

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
    public async Task PauseAsync()
    {
        await StopLoopAsync();
        Status = GameStatus.Paused;
    }

    /// <summary>Advance exactly one generation while paused.</summary>
    public Generation Step() => _engine.Advance();

    /// <summary>Any state → torn down: stop the loop and await its shutdown.</summary>
    public Task StopAsync() => StopLoopAsync();

    private void StartLoop(GameStatus runningStatus)
    {
        Status = runningStatus;
        _loopCts = new CancellationTokenSource();
        _loopTask = RunAsync(_loopCts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_period);
            while (await timer.WaitForNextTickAsync(cancellationToken))
                _engine.Advance();
        }
        catch (OperationCanceledException)
        {
            // Expected on pause/stop.
        }
    }

    private async Task StopLoopAsync()
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
