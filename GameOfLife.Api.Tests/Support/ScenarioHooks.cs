using Reqnroll;

namespace GameOfLife.Api.Tests.Support;

/// <summary>
/// Scenario lifecycle for the BDD suite. The shared <see cref="ApiTestContext"/> world is
/// context-injected here (the same instance the drivers received) so it can be torn down once,
/// in one place. Teardown is async on purpose: Reqnroll's container disposes synchronously, but the
/// world owns SignalR long-polling connections that must be closed via <c>DisposeAsync</c> to avoid
/// the flaky sync-over-async teardown this suite disables parallelism to guard against.
/// </summary>
[Binding]
public sealed class ScenarioHooks(ApiTestContext ctx)
{
    [AfterScenario]
    public async Task DisposeWorld() => await ctx.DisposeAsync();
}
