using GameOfLife.Api.Contracts;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests.Drivers;

/// <summary>
/// The observer's side of the single-game vertical: connects a real SignalR observer over the shared
/// <see cref="ApiTestContext"/> world and exposes what it was told. Acting and waiting live here;
/// the boolean/equality assertions live in the steps (drivers stay assertion- and framework-free).
/// Reqnroll context-injects this and the same world instance per scenario.
/// </summary>
public sealed class ObserverDriver(ApiTestContext ctx)
{
    private ObserverClient? _observer;

    private ObserverClient Observer =>
        _observer ?? throw new InvalidOperationException("Observer is not connected; call Connect() first.");

    /// <summary>The lifecycle statuses this observer has been pushed, in order.</summary>
    public IReadOnlyList<GameStatus> Statuses => Observer.Statuses;

    /// <summary>The births/deaths deltas this observer has been pushed, in order.</summary>
    public IReadOnlyList<DeltaDto> Deltas => Observer.Deltas;

    /// <summary>Opens the observer's SignalR connection over the in-memory world.</summary>
    public async Task Connect()
    {
        _observer = await ctx.ConnectObserverAsync();
    }

    /// <summary>Waits until the given status has been observed (or times out); returns whether it was seen.</summary>
    public Task<bool> WaitForStatus(GameStatus status) =>
        Observer.WaitForAsync(o => o.Statuses.Contains(status));
}
