# GameOfLife

A distributed Conway's Game of Life: an ASP.NET Core API runs the simulation on a
2^64 × 2^64 torus and pushes coalesced cell deltas to a Blazor WebAssembly client
over SignalR. One admin controls the game (start / pause / resume / step / stop);
any number of observers watch live.

The code in this repo is created using AI agents utilizing the skills provided by
Matt Pocock — https://github.com/mattpocock/skills

## Projects

| Project | What it is |
|---|---|
| `GameOfLife.Core` | The engine — sparse HashSet-based Life on the torus. No ASP.NET dependency. |
| `GameOfLife.Api` | HTTP control surface + SignalR hub (`/hubs/game`) + the coalesced broadcast loop. |
| `GameOfLife.WebClient` | Blazor WASM client — subscribe-first attach, snapshot bootstrap, delta apply, canvas viewport. |

## Running in development

Two processes. From the repo root:

```bash
dotnet run --project GameOfLife.Api
```

```bash
dotnet run --project GameOfLife.WebClient
```

- API: `http://localhost:5092`
- Client: `http://localhost:5292` (opens automatically)

The client's backend address lives in `GameOfLife.WebClient/wwwroot/appsettings.json`;
the API's CORS allow-list and broadcast cadence live in `GameOfLife.Api/appsettings.json`.

---

# Performance notes

## The symptom

With a **growing** pattern (e.g. the Gosper glider gun preset — gliders escape on the
infinite torus and never collide, so the live population grows linearly forever), the
client would visibly stutter once a session passed **~1,200 generations**.

## Where it actually lives

The whole runtime data path is bounded by **current population**, not generation count —
the engine keeps no history, and each broadcast is a *net* diff since the last one. Measuring
the engine directly (worst-case gun seed) confirms the backend is never the bottleneck:

| gen | population | delta cells / msg | JSON bytes / msg | engine step |
|----:|-----------:|------------------:|-----------------:|------------:|
| 1,200 | ~236 | ~199 | ~7 KB | < 0.1 ms |
| 5,000 | ~884 | ~704 | ~27 KB | ~0.5 ms |

Server serialization and simulation stay cheap. The cost is entirely **client-side managed
code running in WebAssembly**, and a browser Performance profile pins it precisely:

- **Dev / `dotnet run` (interpreted):** ~**94%** of self-time collapses into a *single*
  `dotnet.native…wasm` function — the Mono **IL interpreter** dispatch loop. `dotnet run`
  is *never* AOT-compiled, so every line of C# per delta is interpreted, 50–100× per second.
  The JS renderer (`delta`, `parseKey`, canvas paint) is **< 1%** combined — not the culprit.
- **AOT publish:** the single interpreter frame **disappears**; cost spreads across dozens of
  real compiled methods, main-thread utilisation drops, and the app runs smoothly **past
  gen 11,600 with ~2,000 live cells**.

**Conclusion:** the "1,200-generation cliff" was overwhelmingly a **dev-mode interpreter
artifact**. Production users on an AOT build never hit it at those generation counts.

## Residual & tuning lever

AOT is a large constant-factor win, not a change of shape: delta size still grows linearly
with population, pushed at a fixed cadence, so a long-enough growing run will eventually
re-saturate the main thread. The cheapest lever is the broadcast cadence —
`Game:BroadcastIntervalMs` in `GameOfLife.Api/appsettings.json` (smaller = more messages/sec
= more client work). Raising it trades update smoothness for headroom; secondary wins are
coalescing the per-delta Blazor re-render and shrinking the delta DTO.

---

# Reproducing the AOT benchmark

Anyone can reproduce the interpreted-vs-AOT result locally.

### 1. Prerequisites

- **.NET 10 SDK** (`dotnet --version` → 10.x).
- **WASM AOT tooling** — required for `RunAOTCompilation`:

  ```bash
  dotnet workload install wasm-tools
  ```

- **A static file server** for the published client. Stay in the .NET toolchain with
  `dotnet-serve`, a global tool that sets the `.wasm` MIME type correctly:

  ```bash
  dotnet tool install --global dotnet-serve
  ```

### 2. Build the client with AOT

From the repo root (the AOT compile step is slow — minutes, not seconds):

```bash
dotnet publish GameOfLife.WebClient -c Release -p:RunAOTCompilation=true -o ./publish-aot
```

The publish log should show `AOT compiling assemblies`. The static site lands in
`./publish-aot/wwwroot`.

### 3. Serve it

The published client is pure static files — no .NET process. It needs the API running and
must be served from a CORS-allowed origin. `http://localhost:5292` is already in the API's
Development allow-list, so serve there:

```bash
dotnet run --project GameOfLife.Api
```

```bash
dotnet serve --directory ./publish-aot/wwwroot --port 5292
```

Then open **`http://localhost:5292`** and navigate into the game from the home page.
(`dotnet serve` has no SPA fallback, so start at the root `/` and use in-app navigation
rather than hard-refreshing a deep route like `/admin`.)

> **CORS note:** the API only allows the origins in `Cors:AllowedOrigins`
> (`GameOfLife.Api/appsettings.json`, plus the Development additions). Serving the client on
> any *other* port means adding that origin there and restarting the API. Keep it on `5292`
> to avoid this. Serve over `http` (not `https`) to match the `http` backend address and
> avoid a mixed-content block.

### 4. Profile

1. Create a game with the **Gosper gun** preset and let it run.
2. Open DevTools → **Performance**, record ~15–25 s once it's past ~1,200 generations.
3. In **Bottom-up**, sort by **Self time**:
   - **Interpreted build** → one `dotnet.native…wasm` function at ~90%+.
   - **AOT build** → cost spread across many `dotnet.native…wasm` frames, none dominant.

To see the interpreted baseline for comparison, just profile the normal
`dotnet run --project GameOfLife.WebClient` dev server (`http://localhost:5292`) instead.
