using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// Covers the fault-observation helper behind the client's intentional fire-and-forget paths. Each case
/// starts from an already-completed antecedent, so the helper's synchronous fault-only continuation has
/// run by the time <c>FireAndForget</c> returns — the assertions are deterministic without any waiting.
/// </summary>
public class FireAndForgetExtensionsTests
{
    [Fact]
    public void Given_a_faulted_task_When_fired_Then_the_fault_is_observed()
    {
        Exception? observed = null;
        var boom = new InvalidOperationException("boom");

        Task.FromException(boom).FireAndForget("ctx", ex => observed = ex);

        Assert.Same(boom, observed);
    }

    [Fact]
    public void Given_a_successful_task_When_fired_Then_no_fault_is_observed()
    {
        var faulted = false;

        Task.CompletedTask.FireAndForget("ctx", _ => faulted = true);

        Assert.False(faulted);
    }

    [Fact]
    public void Given_a_cancellation_fault_When_fired_Then_it_is_swallowed()
    {
        var faulted = false;

        Task.FromException(new OperationCanceledException()).FireAndForget("ctx", _ => faulted = true);

        Assert.False(faulted);
    }

    [Fact]
    public void Given_a_disposed_object_fault_When_fired_Then_it_is_swallowed()
    {
        var faulted = false;

        Task.FromException(new ObjectDisposedException("module")).FireAndForget("ctx", _ => faulted = true);

        Assert.False(faulted);
    }

    [Fact]
    public void Given_a_faulted_value_task_When_fired_Then_the_fault_is_observed()
    {
        Exception? observed = null;
        var boom = new InvalidOperationException("boom");

        new ValueTask(Task.FromException(boom)).FireAndForget("ctx", ex => observed = ex);

        Assert.Same(boom, observed);
    }
}
