---
id: 0001
title: F# on both server and client (Fable transpilation)
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0001: F# on both server and client (Fable transpilation)

> Backfill — documents a decision the project was built on. Status `accepted` reflects "in production", not a fresh choice.

## Context

Mediatheca is a single-user, single-developer side project. The author writes F# fluently and wants the same language end-to-end so:
- Domain types (events, commands, DTOs) are shared by reference between server and client, not mirrored.
- Refactors that change a domain shape ripple through both layers via the compiler.
- The cost of context-switching across languages is eliminated.

The client must run in browsers, including mobile.

## Decision

Use **F# on the server** (.NET 9 / Giraffe) and **F# on the client** (Fable transpiling to JavaScript, Feliz for React bindings, Elmish for MVU). The `src/Shared/` project is compiled into both targets; its types are the API contract.

## Consequences

### Positive
- Domain types literally identical across server and client. No DTO mismatch class of bug.
- Discriminated unions and exhaustive match on both sides.
- One language to be expert in.
- Fable.Remoting gives type-safe RPC essentially for free (see [[0004-fable-remoting]]).

### Negative
- Tooling friction: vite-plugin-fable version-pinning, occasional ESM import quirks (e.g. `ts-lsp-client@1.1.0` issue noted in CLAUDE.md), build-time Fable compilation step.
- The F# / JS interop surface is real when consuming JS libraries; some React components feel awkward through Feliz.
- Smaller ecosystem and community vs. TypeScript on the frontend.
- Hiring would be hard. (Not a concern here — single-developer project.)

### Neutral
- Compile times are longer than a pure TS frontend; bearable in dev with vite-plugin-fable.
- Source maps work but stack traces still reference Fable-compiled names sometimes.

## Alternatives considered

- **F# server + TypeScript client** — would have required maintaining parallel DTOs and a separate API contract layer. Rejected to keep types literally shared.
- **TypeScript end-to-end** — would have meant abandoning F#'s domain-modeling strengths (DUs, exhaustive matching, units of measure). Not acceptable for the author.
- **C# server + Fable client** — F# is available on .NET anyway and gives much better domain modeling. C# adds nothing here.

## References

- `src/Shared/Shared.fs` — the shared types in action.
- `CLAUDE.md` § "Tech Stack", § "Gotchas".
