using GameOfLife.Api.Game;
using GameOfLife.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The two server background loops must survive non-cancellation faults, not fail silently. The sim
/// loop turns a faulting tick into a logged, observable stop instead of a frozen game still reporting
/// Running; the broadcast loop rides out a single failing broadcast instead of tearing down the host.
/// These drive the internal loop types directly (InternalsVisibleTo) rather than the full HTTP host.
/// </summary>
public class BackgroundLoopRecoveryTests
{
    [Fact]
    public async Task Given_a_running_game_When_its_engine_tick_throws_Then_the_session_ends_terminal_and_notifies_the_host_not_stuck_running()
    {
        var faultSignalled = new TaskCompletionSource();
        GameSession? faultedArg = null;

        var session = new GameSession(
            new ThrowingSimulationEngine(),
            Rule.Parse("B3/S23"),
            tickRate: 100, // 10ms period — the first tick fires (and throws) promptly.
            autoStart: true,
            NullLogger<GameSession>.Instance,
            faulted =>
            {
                faultedArg = faulted;
                faultSignalled.TrySetResult();
                return Task.CompletedTask;
            },
            onAdvanced: () => { }); // The throwing engine never completes an advance, so this never fires.

        var winner = await Task.WhenAny(faultSignalled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(winner, faultSignalled.Task), "the faulted loop never handed off to the host");

        // The session drove the host fault sink with itself, transitioned off Running, and landed on the
        // terminal NoGame state the host then pushes to observers.
        Assert.Same(session, faultedArg);
        Assert.NotEqual(GameStatus.Running, session.Status);
        Assert.Equal(GameStatus.NoGame, session.Status);

        // Teardown after a fault is idempotent and must not throw.
        await session.Stop();
    }

    [Fact]
    public async Task Given_a_broadcast_that_throws_once_When_the_loop_ticks_Then_subsequent_broadcasts_still_run()
    {
        var broadcaster = new FlakyBroadcaster(throwOnCall: 1);
        var service = new BroadcastLoopService(broadcaster, NullLogger<BroadcastLoopService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // The first broadcast throws; the loop must survive it and keep pulsing.
            var keptGoing = await broadcaster.ReachedCalls(3, TimeSpan.FromSeconds(5));
            Assert.True(keptGoing, $"broadcast loop stopped after the failure; only {broadcaster.CallCount} calls");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(broadcaster.Threw, "expected the first broadcast to have thrown");
    }

    /// <summary>A tick that always faults; its <see cref="Current"/> is never reached in these tests.</summary>
    private sealed class ThrowingSimulationEngine : ISimulationEngine
    {
        public Generation Current => throw new NotSupportedException("the throwing test engine publishes no generation");

        public Generation Advance() => throw new InvalidOperationException("simulated engine failure");
    }

    /// <summary>Throws on the Nth call, counts every call, and lets a test await a target call count.</summary>
    private sealed class FlakyBroadcaster(int throwOnCall) : IBroadcaster
    {
        private int _calls;

        public int CallCount => Volatile.Read(ref _calls);

        public bool Threw { get; private set; }

        // Stands in for the advance signal: a small delay per generation, so the loop drives repeated
        // broadcasts (exercising recovery after a throwing one) without a real simulation and without
        // hot-spinning now that the pump has no throttle of its own.
        public Task WaitForPending(CancellationToken cancellationToken) => Task.Delay(5, cancellationToken);

        public Task BroadcastPending()
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == throwOnCall)
            {
                Threw = true;
                throw new InvalidOperationException("simulated broadcast failure");
            }

            return Task.CompletedTask;
        }

        public async Task<bool> ReachedCalls(int target, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (CallCount >= target) return true;
                await Task.Delay(20);
            }

            return CallCount >= target;
        }
    }
}
