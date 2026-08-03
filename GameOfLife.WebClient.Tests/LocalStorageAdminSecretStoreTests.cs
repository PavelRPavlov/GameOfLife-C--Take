using GameOfLife.WebClient.Communication;
using Microsoft.JSInterop;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// Drives <see cref="LocalStorageAdminSecretStore"/> against a fake in-process JS runtime standing in
/// for the browser's <c>localStorage</c>: synchronous hydration on first read, write-through on
/// set/clear, and the graceful degradation when the runtime is not in-process (pre-hydration, e.g.
/// server prerender).
/// </summary>
public sealed class LocalStorageAdminSecretStoreTests
{
    [Fact]
    public void Given_a_secret_in_localStorage_When_Current_is_read_the_first_time_Then_it_hydrates_once_and_caches()
    {
        var js = new FakeInProcessJsRuntime();
        js.Storage["gol.adminSecret"] = "persisted-secret";
        var store = new LocalStorageAdminSecretStore(js);

        Assert.True(store.HasSecret);
        Assert.Equal("persisted-secret", store.Current);
        Assert.Equal(1, js.GetItemCalls); // hydrated once, then cached
        _ = store.Current;
        Assert.Equal(1, js.GetItemCalls); // second read serves the cache — no re-hydration
    }

    [Fact]
    public void Given_empty_localStorage_When_Current_is_read_Then_it_is_null()
    {
        var store = new LocalStorageAdminSecretStore(new FakeInProcessJsRuntime());

        Assert.False(store.HasSecret);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Given_a_store_When_SetAsync_is_called_Then_it_writes_through_and_caches()
    {
        var js = new FakeInProcessJsRuntime();
        var store = new LocalStorageAdminSecretStore(js);

        await store.Set("fresh-secret");

        Assert.Equal("fresh-secret", js.Storage["gol.adminSecret"]); // written through to localStorage
        Assert.Equal("fresh-secret", store.Current);                  // cached — no getItem needed
        Assert.Equal(0, js.GetItemCalls);
    }

    [Fact]
    public async Task Given_a_stored_secret_When_ClearAsync_is_called_Then_it_removes_the_key_and_caches_null()
    {
        var js = new FakeInProcessJsRuntime();
        js.Storage["gol.adminSecret"] = "to-be-cleared";
        var store = new LocalStorageAdminSecretStore(js);

        await store.Clear();

        Assert.False(js.Storage.ContainsKey("gol.adminSecret")); // removed from localStorage
        Assert.False(store.HasSecret);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Given_a_runtime_that_is_not_in_process_When_Current_is_read_Then_it_stays_null()
    {
        // A non-in-process runtime (e.g. prerender): the synchronous read is skipped, so nothing hydrates.
        var store = new LocalStorageAdminSecretStore(new FakeAsyncOnlyJsRuntime());

        Assert.False(store.HasSecret);
        Assert.Null(store.Current);
    }

    /// <summary>An in-memory stand-in for the Wasm in-process runtime backed by a dictionary.</summary>
    private sealed class FakeInProcessJsRuntime : IJSInProcessRuntime
    {
        public Dictionary<string, string> Storage { get; } = new();
        public int GetItemCalls { get; private set; }

        public TResult Invoke<TResult>(string identifier, params object?[]? args)
        {
            if (identifier == "localStorage.getItem")
            {
                GetItemCalls++;
                var key = (string)args![0]!;
                var value = Storage.TryGetValue(key, out var v) ? v : null;
                return (TResult)(object?)value!;
            }
            throw new NotSupportedException(identifier);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            ApplyVoid(identifier, args);
            return new ValueTask<TValue>(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        private void ApplyVoid(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.setItem":
                    Storage[(string)args![0]!] = (string)args[1]!;
                    break;
                case "localStorage.removeItem":
                    Storage.Remove((string)args![0]!);
                    break;
                default:
                    throw new NotSupportedException(identifier);
            }
        }
    }

    /// <summary>An async-only runtime (not <see cref="IJSInProcessRuntime"/>) — the pre-hydration case.</summary>
    private sealed class FakeAsyncOnlyJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => new(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => new(default(TValue)!);
    }
}
