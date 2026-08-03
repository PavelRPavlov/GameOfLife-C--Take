using Microsoft.AspNetCore.Components;

namespace GameOfLife.WebClient;

/// <summary>
/// Base for components that mirror the live <see cref="GameStore"/> onto the DOM: it subscribes to the
/// store's status/generation change events on init, marshals each onto the renderer's sync context with
/// <see cref="ComponentBase.InvokeAsync(Action)"/> → <see cref="ComponentBase.StateHasChanged"/>, and
/// unsubscribes on dispose. Extracted from Home/Observe/Admin/GameStage, which repeated this block verbatim.
/// </summary>
/// <remarks>
/// The base owns ONLY the shared status/generation → re-render lifecycle. Components with extra behavior
/// (e.g. the <c>_attached</c> re-arm on teardown) override <see cref="OnInitialized"/> / <see cref="Dispose"/>,
/// call <c>base</c>, and add their own subscriptions — the base never reaches into that.
/// </remarks>
public abstract class StoreBoundComponentBase : ComponentBase, IDisposable
{
    [Inject] protected GameStore Store { get; set; } = default!;

    protected override void OnInitialized()
    {
        Store.StatusChanged += OnStoreChanged;
        Store.GenerationChanged += OnStoreChanged;
    }

    private void OnStoreChanged(GameStatus _) => InvokeAsync(StateHasChanged);

    private void OnStoreChanged(long _) => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        Store.StatusChanged -= OnStoreChanged;
        Store.GenerationChanged -= OnStoreChanged;
    }
}
