# Vertical-slice structure for GameOfLife.Api

## Status

accepted

## Context and decision

`GameOfLife.Api` is organized by **feature (vertical slice)** rather than by technical layer.
The old `Contracts/` + `Hosting/` + `Hubs/` layering is replaced by `Features/` (one folder per
feature, owning its request/response DTOs and endpoint handler), a shared `Game/` kernel, a shared
`Contracts/` folder for cross-slice wire vocabulary, and a `Composition/` root that wires the app up.
`Program.cs` is now a thin orchestrator: `AddGame().AddGameApiSurface()` → `UseGameApiPipeline()` →
`MapGameEndpoints()`. This is the standing structural direction for the project's other assemblies
too, applied as each is next worked (this pass converted the API only).

Several of the choices are deliberate and would otherwise look inconsistent, so they are recorded here.

## Considered options / the non-obvious calls

- **Central route registrar, not self-registering slices.** All routes are mapped in one place
  (`Composition/EndpointRegistration.MapGameEndpoints`) pointing at each slice's static handler,
  rather than each slice registering its own route. Routing stays auditable in one file; slices own
  only their handler bodies. This trades a little slice autonomy for centralized, greppable wiring.

- **Module-owned service registration, not one central bundle.** In contrast to routing, DI
  registration is distributed: the kernel exposes `Game/AddGame()` (host, broadcast loop, SignalR
  hub + its JSON), and the REST/OpenAPI/CORS edge is `Composition/AddGameApiSurface()`. So wiring is
  split on purpose — routes centralized, services module-owned. Each half reads where its concern lives.

- **Five control verbs are five dedicated handlers in one `GameControl/` folder**, not five folders
  and not one merged handler. `start/stop/pause/resume/step` share `ControlResponse` and a
  `ControlDispatch` (read `X-Admin-Secret` → run verb → map outcome), all co-located in the one slice.

- **Streaming is kernel infrastructure, not a slice.** The broadcast engine is bound to `GameHost`'s
  state/broadcast gates and session lifecycle, so `GameHub`, `IGameClient`, `DeltaDto`,
  `BroadcastLoopService`, and the delta computation live in `Game/`. Feature slices are the HTTP
  endpoints only; they don't touch the push channel. (Extracting a `Broadcaster` and inverting the
  dependency is a legitimate *future* ADR if streaming grows — e.g. multiple games or per-client
  subscriptions — but was out of scope here.)

- **Kernel depends on nobody; DTOs split along the kernel/HTTP line.** `GameHost` returns kernel
  contracts — `ControlOutcome`/`ControlResult` and the `GameSnapshot` read-model — which live in
  `Game/`. The HTTP projections (`ControlResponse`, `SnapshotResponse`) live in their slices, which
  map from the kernel types. This keeps every dependency arrow pointing slice → kernel → `Contracts`,
  never kernel → feature. It is the reason `GetSnapshotAsync` no longer builds `SnapshotResponse`
  directly and `ControlOutcome` is not in `GameControl/`.
