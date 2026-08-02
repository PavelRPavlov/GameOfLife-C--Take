# Running the WebClient (and recovering from `_framework/*.js` 404)

> **Rule for Claude / AI assistants: never run these apps yourself.**
> Do not start, run, or launch the backend (`GameOfLife.Api`) or the frontend
> (`GameOfLife.WebClient`) — no `dotnet run`, no dev server, no background process, directly or
> via a subagent. If a task needs a running app, stop and ask Pavel to start it manually, then
> continue once he confirms it is up. Building, testing, and editing code is fine; only
> *running* the apps is off-limits. The commands below are for a human to run.

The `GameOfLife.WebClient` is a **standalone** Blazor WebAssembly app served by the
`Microsoft.AspNetCore.Components.WebAssembly.DevServer`. It talks to a **separate** backend
process — it is not a hosted (server-rendered) Blazor app.

## Ports

| Process | Port | Source |
|---|---|---|
| WebClient (Blazor Wasm dev server) | `http://localhost:5292` | `GameOfLife.WebClient/Properties/launchSettings.json` (`http` profile) |
| Backend API | `http://localhost:5092` | `GameOfLife.WebClient/wwwroot/appsettings.json` → `BackendBaseAddress` |

The WebClient reads `BackendBaseAddress` at startup, so the API must be reachable on `:5092`
for the app to do anything beyond render its shell.

## Running it

Start the API first, then the WebClient:

```bash
dotnet run --project GameOfLife.Api/GameOfLife.Api.csproj
```

```bash
dotnet run --project GameOfLife.WebClient/GameOfLife.WebClient.csproj --launch-profile http
```

Then open `http://localhost:5292`. (A `webclient` config in `.claude/launch.json` starts the
WebClient on `:5292` for tooling.)

## Failure mode: "Failed to start platform" / `_framework/*.js` 404

### Signature

The page never boots and the browser console shows:

```
GET http://localhost:5292/_framework/dotnet.<hash>.js  404 (Not Found)
Uncaught (in promise) Error: Failed to start platform.
  Reason: TypeError: Failed to fetch dynamically imported module: .../_framework/dotnet.<hash>.js
```

### What it means

This is **not** a missing asset or a broken `index.html`. The WebClient uses .NET 10
content-fingerprinted assets (`OverrideHtmlAssetPlaceholders=true`): the loader, importmap, and
preload in `index.html` are filled in at build time and reference files like
`dotnet.<hash>.js` by their content hash.

The 404 almost always means **a stale dev-server process is still bound to `:5292`, serving a
build that no longer matches the files on disk.** A rebuild regenerated `wwwroot/_framework`, but
the old server (started before the rebuild) is still answering requests with its now-invalid
asset map. The file the browser asks for genuinely exists in `bin/.../wwwroot/_framework/` — the
*running server* just doesn't know how to serve it.

Confirm by checking who owns the port and when it started, versus when the build output was
produced:

```bash
netstat -ano | grep ':5292'
powershell -NoProfile -Command "Get-Process -Id <PID> | Select-Object Id,StartTime"
```

If the process `StartTime` predates your last build, that's the culprit.

### Recovery

1. **Kill the stale server** holding `:5292` (the PID from `netstat` above).
2. **Clean rebuild** so no half-stale assets survive: `dotnet clean && dotnet build` in
   `GameOfLife.WebClient/`.
3. **Restart** the dev server (see "Running it").
4. **Hard-refresh** the browser (Ctrl+F5 / empty cache and reload) so it drops any cached
   importmap or boot resources pointing at the old build.

One-liner mental model: **a `_framework/*.js` 404 means your server is older than your build —
restart the server, don't hunt for the file.**
