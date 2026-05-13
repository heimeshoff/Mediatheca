---
id: 0004
title: Fable.Remoting for type-safe client/server RPC
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0004: Fable.Remoting for type-safe client/server RPC

> Backfill — the RPC mechanism for the full-stack F# setup.

## Context

With F# on both sides ([[0001-fsharp-fullstack]]), the cheapest way to expose server behavior to the client is a typed RPC layer that reuses the shared types directly. Hand-rolling REST endpoints + JSON serialization on both ends would duplicate every signature and re-introduce the type-mismatch class of bug we paid for by going full-stack F# in the first place.

The app is single-user, single-origin, single-domain. There is no public API surface and no third-party consumer. Versioning concerns are minimal.

## Decision

Use **Fable.Remoting** to expose server methods to the client. The shared contract is `IMediathecaApi` in `src/Shared/Shared.fs` — one interface listing every server method, with parameters and return types in pure F#. The server implements it; the client gets a typed proxy. Fable.Remoting handles serialization and dispatch, mounted under `/api/{TypeName}/{MethodName}`.

## Consequences

### Positive
- API contract is a single F# interface. Adding a method = adding one line + an implementation; both client and server compile or fail together.
- Discriminated unions and `Option` cross the wire transparently.
- No manual JSON contract bookkeeping.
- IntelliSense reaches across the boundary in the IDE.

### Negative
- Not REST. Tools that assume REST (Postman, curl explorations, Swagger docs) don't fit naturally.
- Couples the client to a server method shape — if the API ever needs to serve non-F# consumers, this layer would need replacement.
- The proxy is opinionated about routing; ad-hoc HTTP needs (file uploads, SSE, etc.) live alongside as plain Giraffe routes.

### Neutral
- Network failure modes are wrapped in Fable.Remoting's exception types; error handling on the client conforms to its model.

## Alternatives considered

- **Hand-rolled REST + manual DTOs** — rejected as duplication.
- **gRPC** — overkill for browser client and adds a separate codegen step.
- **GraphQL** — adds schema duplication and a query language for no benefit in a single-frontend single-user app.

## References

- `IMediathecaApi` in `src/Shared/Shared.fs`.
- `src/Server/Api.fs` — implementation.
- `CLAUDE.md` § "Tech Stack".
