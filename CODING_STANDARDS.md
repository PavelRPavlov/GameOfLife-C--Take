# Coding Standards

Conventions for this repo, on top of whatever the compiler and analyzers already enforce.
The `/code-review` **Standards** axis reads this file.

## Naming

### No `Async` suffix on our own async methods

Do **not** suffix an asynchronous method we own with `Async`. Name the method for what
it does, not for how it returns.

```csharp
// no
public async Task<GameSnapshot?> GetSnapshotAsync() { ... }
await host.GetSnapshotAsync();

// yes
public async Task<GameSnapshot?> GetSnapshot() { ... }
await host.GetSnapshot();
```

This applies to method **declarations** and every **call site** of a method we define —
across `.cs` and `.razor` (including `@code` blocks and `@onclick` handlers).

**Exception — names the framework dictates.** Keep the suffix only where a base class or
interface we implement already named the method that way; renaming it would break the
contract. Do not invent new `Async`-suffixed names, and never rename a framework method we
merely *call*. Concretely, these stay as-is:

- `DisposeAsync` — `IAsyncDisposable` / `IAsyncLifetime`
- `InitializeAsync` — `IAsyncLifetime` (test fixtures)
- `ExecuteAsync` — `BackgroundService`
- `StartAsync` / `StopAsync` — `IHostedService` (only when implementing that interface)
- `SendAsync` — `HttpMessageHandler`
- Any `*Async` method defined by the BCL or a NuGet package that we call
  (`HttpClient.PostAsync`, `HubConnection.StartAsync`, `IJSRuntime.InvokeAsync`,
  `WebAssemblyHost.RunAsync`, `HttpContent.ReadFromJsonAsync`, …)

Rule of thumb: if we wrote the method's name, drop `Async`; if the framework wrote it, leave it.
