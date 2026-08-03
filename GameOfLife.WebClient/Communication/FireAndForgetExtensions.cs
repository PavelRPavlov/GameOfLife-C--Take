namespace GameOfLife.WebClient.Communication;

/// <summary>
/// Fault-observation for the handful of intentional fire-and-forget async paths in the client. The Blazor
/// Wasm consumer is single-threaded and these paths are <see cref="Result{T,E}"/>-wrapped, so a fault is
/// not expected — but an un-awaited <see cref="Task"/> that throws becomes an <em>unobserved</em>
/// TaskException with no surface at all. Attaching a fault-only continuation keeps the call site
/// fire-and-forget while ensuring an unexpected fault is reported (to the browser console by default)
/// instead of vanishing. Cancellation and post-disposal teardown races are expected on these paths and
/// are swallowed. The <c>onFault</c> parameter is a test seam; production leaves it null.
/// </summary>
internal static class FireAndForgetExtensions
{
    /// <summary>Observe <paramref name="task"/> without awaiting it (see the type summary).</summary>
    public static void FireAndForget(this Task task, string context, Action<Exception>? onFault = null) =>
        task.ContinueWith(
            t =>
            {
                var ex = t.Exception!.GetBaseException();
                // Cancellation and disposed-object races are the expected non-faults on these paths.
                if (ex is OperationCanceledException or ObjectDisposedException) return;
                if (onFault is not null) onFault(ex);
                else Console.Error.WriteLine($"[{context}] unobserved fire-and-forget fault: {ex}");
            },
            CancellationToken.None,
            // OnlyOnFaulted: the continuation runs only on a fault, so t.Exception is always non-null.
            // ExecuteSynchronously keeps it inline on Blazor's single thread (no scheduler round-trip).
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// <see cref="ValueTask"/> overload for JS-interop call sites (<see cref="IJSObjectReference"/> returns
    /// <see cref="ValueTask"/>). Materializes to a <see cref="Task"/> once, then observes it as above.
    /// </summary>
    public static void FireAndForget(this ValueTask task, string context, Action<Exception>? onFault = null) =>
        task.AsTask().FireAndForget(context, onFault);
}
