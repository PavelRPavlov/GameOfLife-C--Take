# Shared error-envelope contract in GameOfLife.Shared

## Status

accepted

## Context and decision

Every expected client-facing API failure — 400 / 403 / 404 / 409 and the redacted 500 — now returns
one bespoke JSON envelope: `{ code, message, errors[] }` as `application/json` (never
`application/problem+json`). `code` is an always-present, machine-readable discriminant (one per
distinct failure *reason*, not per HTTP status); `message` is always-present user-presentable copy the
client shows verbatim; `errors[]` is always present (`[]` for single-error cases, populated only for
`VALIDATION_FAILED`).

A new **framework-free class library `GameOfLife.Shared`** (no ASP.NET, no Blazor) holds *both* the
`ErrorCodes` string constants *and* the envelope DTOs (`ErrorEnvelope`, `FieldError`). It is referenced
by both `GameOfLife.Api` and `GameOfLife.WebClient`; neither the server request/response DTOs nor the
client mirrors redefine the envelope. The server tags every failure with an `ErrorCodes` constant; the
client deserializes the envelope and branches on `code` into its `GameError` union, showing `message`
directly. User-facing *copy* lives server-side only (a static `ErrorMessages` in `GameOfLife.Api`); the
client reads `message` off the wire and never references these strings, so `Shared` carries only the
contract.

## The deliberate exception to ADR 0002

[ADR 0002](0002-shared-primitive-kernel-browser-in-webclient.md) fixes the rule as **domain primitives
shared, wire contracts owned by each tier** — even the atomic `CellDto` stays duplicated rather than
half-sharing the contract. The error envelope is a **deliberate, documented exception** to that rule:

- `code` is a *compile-time contract both tiers must agree on* — the client's error handling depends on
  the exact string values, not on inferred status-by-endpoint semantics. Duplicating `ErrorCodes` and
  the envelope DTOs per tier would let the catalogue drift silently, which is precisely the coupling
  this contract removes. So these types are **shared** where request/response DTOs are **not**.
- The split stays clean: `Shared` holds the contract (`ErrorCodes` + DTOs) and nothing else. Copy
  (`ErrorMessages`) is *not* contract and stays in `Api`. On the error-surfacing path the client authors
  exactly one string — the no-envelope transport fallback (`"Couldn't reach the server. Please try
  again."`); every other message is the server's, read off the wire and shown verbatim. (The
  pre-existing client-side pre-send *validation guard* in `HttpGameApi` still mirrors the server's field
  copy as a courtesy, so a short-circuit reads like a server rejection — those strings are guard copy,
  not the wire contract, per the source spec.)
- `Shared` is kept **out of `GameOfLife.Core`** so the domain kernel stays transport-free (ADR 0002 —
  `Core` holds only `Cell`, `GameStatus`, `Rule`). Error-contract types are a cross-cutting *transport*
  concern, consistent with ADR 0001's kernel/cross-cutting split, so they live in their own shared
  library rather than a feature slice or the domain kernel.

## Consequences

- `ErrorCodes` values are a public contract: **additive-only**, frozen for life, never renamed or
  repurposed. New failure reasons get brand-new codes with zero client coordination; clients must
  tolerate an unknown code by falling back to `message`.
- The redacted 500 emits the same envelope (`code: "INTERNAL_ERROR"`, generic message, `errors: []`)
  and no longer speaks `problem+json`; the now-unused `ProblemDetails` registration is removed from
  `GameOfLife.Api`. Development still serves the Developer Exception Page with full traces.
- `message` is deliberately kept off the contract, so a later `Accept-Language` / resource-file
  localization layer is a clean second step with zero client or contract impact.
