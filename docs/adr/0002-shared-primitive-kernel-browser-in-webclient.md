# Shared-primitive kernel in Core; browser code lives in WebClient

## Status

accepted

## Context and decision

The `GameOfLife.Client` assembly is dissolved. Rather than literally merging `Core` and
`Client`, we split the merge along the **network tier**. `GameOfLife.Core` becomes a shared
kernel referenced by *both* the server (`Api`) and the browser (`WebClient`), but it holds only
the three genuinely-shared domain primitives — `Cell`, `GameStatus`, `Rule` — alongside the
existing server-side engine (`GameEngine`, `Generation`). All browser orchestration (`GameStore`,
`HttpGameApi`, the seam interfaces, `Result`/`GameError`, the client wire and domain DTOs, and the
one JS-interop class `LocalStorageAdminSecretStore`) moves into `GameOfLife.WebClient`, grouped
under a `Communication/` folder kept separate from rendering (`Pages/`, `Layout/`). `WebClient` and
`Api` each reference `Core`; neither references the other.

This carries forward ADR 0001's "standing structural direction for the project's other assemblies"
and fixes the cross-assembly boundary the client work had left open.

## Considered options / the non-obvious calls

- **Not a literal `Core` + `Client` merge.** `Core` is the server's dependency (`Api → Core`).
  Folding the whole browser client into `Core` would make the server compile and ship a
  REST-client-to-itself (`HttpGameApi`) plus the browser reconcile engine (`GameStore`), fusing the
  two deployables' dependency graphs and destroying `Core`'s "depends on nobody" property. Instead
  only shared primitives go *up* into `Core`; browser code goes into the *browser* assembly.

- **The kernel is deliberately tiny — three primitives, not "the contracts."** `Cell` and
  `GameStatus` were duplicated verbatim across the tiers and are unified in `Core`; `HttpGameApi`'s
  hand-rolled B/S rule validation is deleted in favour of `Core.Rule.TryParse`. Everything else that
  *looked* shared is not.

- **Wire request/response DTOs stay per-tier, by design.** The server's request/response types
  carry producer concerns — `[Required]`/`IValidatableObject`, `JsonUnmappedMemberHandling`,
  `HubUrl`/`SnapshotUrl`, `ToParameters() → GameParameters` — while the client's are minimal consumer
  mirrors that deliberately drop fields the server emits. The two agree on the *JSON shape* but not
  on the *C# type*, so they are not unified. The rule is **domain primitives shared, wire contracts
  owned by each tier**. Even the atomic `CellDto` stays duplicated rather than half-sharing the
  contract (sharing only the coordinate would muddy the rule to save two lines).

- **Browser code folds into the Wasm `WebClient`, accepting a testability cost.** The former
  `Client` was a plain SDK library, so `GameStore`/`HttpGameApi` were unit-testable off the Wasm
  host. Folding them into the `BlazorWebAssembly` project means the test project now references a
  Wasm-SDK assembly (bUnit-style) to exercise them. Accepted in exchange for a single browser
  assembly and a clean communication/rendering split; only `LocalStorageAdminSecretStore` ever truly
  needed the browser host.

- **The engine rides along in the browser bundle as dead weight.** `WebClient → Core` pulls in
  `GameEngine`/`Generation`, which the browser never runs; the Wasm IL trimmer drops them on publish.
  Preferred over splitting the shared primitives into yet another assembly, which would fight the
  "fewer projects / simplify" goal that motivated this change.

## Consequences

- `GameOfLife.Client.csproj` is removed. `GameOfLife.Client.Tests` becomes the `WebClient` test
  project and references `WebClient`.
- `Api.Contracts.GameStatus` and `Client`'s copy of `GameStatus` are deleted; both tiers use
  `Core.GameStatus`. The duplicated `Cell` collapses to `Core.Cell`.
