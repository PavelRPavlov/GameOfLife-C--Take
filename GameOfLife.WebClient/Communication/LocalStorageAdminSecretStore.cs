namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The real <see cref="IAdminSecretStore"/>, persisting the one-time admin secret in the browser's
/// <c>localStorage</c> so it survives a reload (the backend has no re-fetch path — a lost secret is lost).
/// The opaque secret token is read only by <see cref="HttpGameApi"/> to set the <c>X-Admin-Secret</c> header;
/// component code decides admin-vs-observer affordances from <see cref="HasSecret"/> alone.
///
/// <para><see cref="Current"/>/<see cref="HasSecret"/> are synchronous, so the value is hydrated from
/// <c>localStorage</c> on first read via the Wasm <see cref="IJSInProcessRuntime"/> and then cached;
/// writes go through <c>localStorage</c> and update the cache.</para>
/// </summary>
public sealed class LocalStorageAdminSecretStore : IAdminSecretStore
{
    private const string Key = "gol.adminSecret";

    private readonly IJSRuntime _js;
    private string? _current;
    private bool _hydrated;

    public LocalStorageAdminSecretStore(IJSRuntime js) => _js = js;

    public bool HasSecret => Current is not null;

    public string? Current
    {
        get
        {
            EnsureHydrated();
            return _current;
        }
    }

    public async Task Set(string secret)
    {
        await _js.InvokeVoidAsync("localStorage.setItem", Key, secret);
        _current = secret;
        _hydrated = true;
    }

    public async Task Clear()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", Key);
        _current = null;
        _hydrated = true;
    }

    private void EnsureHydrated()
    {
        if (_hydrated)
            return;

        // On Wasm the runtime is always in-process, so the synchronous read backing the sync getters is safe.
        if (_js is IJSInProcessRuntime inProcess)
            _current = inProcess.Invoke<string?>("localStorage.getItem", Key);

        _hydrated = true;
    }
}
