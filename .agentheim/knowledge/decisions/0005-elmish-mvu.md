---
id: 0005
title: Elmish (MVU) as the client architecture
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0005: Elmish (MVU) as the client architecture

> Backfill — the client-side state management pattern.

## Context

With Fable + Feliz wrapping React, the client needs a state-management story. React's own hooks-based local-state model fits screen-local concerns but doesn't give a coherent app-wide control flow. The domain is rich (multi-tab dashboard, modal flows, optimistic mutations against the server) and benefits from a single update loop.

The author is comfortable in MVU patterns from Elm and prefers explicit messages over implicit reactivity for app-level state.

## Decision

Use **Elmish (Model–View–Update)** as the client's top-level architecture. Each page is a child MVU module (`Pages/<Name>/Types.fs|State.fs|Views.fs`); the root MVU (`Types.fs|State.fs|Views.fs`) delegates to child pages via `Cmd.map`. Feliz/React renders the View; messages flow up through the root.

## Consequences

### Positive
- Single, explicit control flow for client state. Every change is a `Msg`.
- Time-travel debugging and reproducible state by construction.
- Child-page pattern (`Types.fs|State.fs|Views.fs`) gives clear file boundaries that scale to many pages.
- Plays well with Fable.Remoting commands ([[0004-fable-remoting]]) via `Cmd.OfAsync.*`.

### Negative
- Boilerplate for trivial local state — every checkbox toggle becomes a `Msg`. Hooks would be lighter.
- Composing many child MVU modules requires careful `Cmd.map` plumbing; easy to mis-wire.
- Component-level React state (e.g. focus management, animations) still uses hooks, so two patterns coexist.

### Neutral
- The root MVU + child pages convention is now a load-bearing structural rule across `src/Client/Pages/`.

## Alternatives considered

- **Feliz + React hooks only** — too unstructured for the multi-page surface area.
- **Sutil** — F# UI library with reactive primitives; smaller ecosystem and the author preferred Elmish's discipline.

## References

- `src/Client/App.fs` — `Program.mkProgram` entry.
- `src/Client/Pages/*/State.fs` for the per-page update functions.
- `CLAUDE.md` § "Project Structure".
