---
id: 0032
title: Compensating events via round-trip validation through each BC's existing serialize/deserialize seam, prefix-dispatched like formatEvent
scope: administration
status: accepted
date: 2026-07-22
supersedes: []
superseded_by: []
related_tasks: [administration-xjmda]
related_research: []
---

# ADR 0032: Compensating events via round-trip validation through each BC's existing serialize/deserialize seam, prefix-dispatched like formatEvent

## Context

ADR-0002 establishes event sourcing as the persistence model: the idiomatic fix for bad
data is appending a corrective event, not mutating history. Before any raw-surgery
tooling exists (administration-wwc36, which this task blocks), the stream drill-in page
needed a safe "Append corrective event" action so the safe path is the easy path.

The central design question: how does the composer turn an operator's edited JSON string
into a legitimate event of a chosen type, for an arbitrary bounded context, without a
second source of truth about each BC's wire format?

Two mechanisms were rejected during refinement:
- **Reflection over event DUs** — the wire format genuinely diverges from the DU shape.
  `Games.Serialization`'s `Game_status_changed` encodes `GameStatus` as a nested string via
  `encodeGameStatus`/`decodeGameStatus` (Games.fs:348-366), and `decodeGameStatus` folds a
  legacy wire value ("Playing") into a current DU case (`InFocus`) that has no reciprocal
  string of the same name. Reflection would be dishonest about what the wire format
  actually is.
- **A hand-maintained template registry** (event type -> example JSON shape) — a second
  source of truth that drifts from the real `Serialization` modules over time, the same
  failure mode `handledEventTypesByBoundedContext` (administration-gxd6e) already works
  around by keeping that registry a literal mirror of match arms, not a reimplementation.

## Decision

Each bounded context already exposes a public `Serialization.serialize: Event -> string *
string` / `deserialize: string -> string -> Event option` pair. Compose these directly:

```fsharp
let codec eventType data = Serialization.deserialize eventType data |> Option.map Serialization.serialize
```

This single composition is simultaneously:
1. **The validation gate** — a payload that doesn't parse into a valid event of that type
   yields `None`; the composer refuses to append (no row inserted, no fallback to raw
   storage).
2. **The canonicalization step** — the bytes actually written to `events.data` are the
   *re-serialized* form, never the operator's raw edit. This is the only way to
   simultaneously support "clone a real event and edit it" (accepting whatever wire shape
   the operator typed, including legacy/loose values) and guarantee the composer's output
   is indistinguishable from an organically-produced event of the same type.

Dispatch to the right BC's codec is by stream-id prefix, using the exact same
`if/elif StartsWith` idiom `EventFormatting.formatEvent` already uses for the timeline's
own formatter dispatch (`EventFormatting.fs:382-390`) — `Administration.eventCodecs`
mirrors `Administration.boundedContextPrefixes`'s prefix strings (kept in sync manually,
the same convention `projectionTables` documents).

**Append path:** `EventStore.appendToStream conn streamId expectedPosition [eventData]` —
the pure-INSERT, expected-position-checked path (`EventStore.fs:381`), never the
explicit-rowid path `importNdjson` uses (that remains empty-store-only territory). Catch-up
reuses the existing `projectionHandlers` list via `Projection.runProjection`, identical to
`Api.executeCommandCore`'s idiom. Audit metadata `{"source":"admin-console"}` is the one
intentional, projection-invisible difference from an organic event.

**Two-call shape (preview then commit), not one:** `previewCompensatingEvent` reads
`expectedPosition` and returns the canonicalized preview for the confirmation dialog;
`appendCompensatingEvent` takes that same `expectedPosition` as a caller-supplied value
(not freshly re-read) and re-validates the payload independently before appending. This
was chosen over a single round-trip call for two reasons: the confirmation dialog needs a
*real* canonical preview to show (not a promise that commit will canonicalize
correctly), and a caller-supplied `expectedPosition` is what makes the optimistic-
concurrency check actually exercisable — a value captured at preview time and honored
unchanged at commit time correctly surfaces a conflict if another append landed on the
stream in between, whereas a freshly-read position at commit would trivially always
match itself. `appendCompensatingEvent` re-validates rather than trusting the preview's
canonical output, so the "never stores an unparseable payload" and "stored bytes are the
canonical round-trip of what was actually committed" invariants hold regardless of what
the client does between the two calls.

**Concurrency gate:** `appendCompensatingEvent`'s append+catch-up body is a fourth
request-reachable `conn.BeginTransaction()` site (via `EventStore.appendToStream`) of the
exact class ADR-0030's `requestDbLock` serializes (alongside `Api.executeCommandCore`,
`GameJournal.save`, `importEventsStreamHandler`). It acquires the same process-wide
`SemaphoreSlim`, threaded into `Administration.create` as a new `dbLock` parameter.

**Pre-fill tiebreak:** "clone a real event" tries the target stream itself first, falling
back BC-prefix-wide only if no instance of the chosen type exists there — an operator
correcting one stream's data most likely wants that stream's own prior shape as the
starting point, not a sibling's.

## Consequences

### Positive
- No second source of truth for wire format — the composer can never drift from what
  each BC's `Serialization` module actually accepts/produces, because it *is* that module.
- The validation-gate and canonicalization concerns collapse into one round-trip,
  eliminating an entire class of "validated but stored differently" bugs.
- Frames administration-wwc36's raw-surgery tooling explicitly as the escape hatch for
  cases this pattern cannot cover (e.g. an event type whose current deserializer
  intentionally rejects a payload shape that historically existed and needs literal
  reproduction) — this task does not attempt to solve that harder problem.

### Negative
- An operator must supply a payload that round-trips through the *current* deserializer;
  reproducing a historical event whose original shape a subsequent deserializer revision
  no longer accepts is out of scope here (that's exactly the wwc36 territory this ADR
  frames).
- `eventCodecs`' prefix strings are a second literal copy of `boundedContextPrefixes`,
  manually kept in sync — an accepted small duplication, matching the existing
  `projectionTables`/`handledEventTypesByBoundedContext` convention rather than
  introducing a new indirection layer for one more registry.

### Neutral
- The two-call (preview/commit) API shape was the worker's chosen default among two
  explicitly-left-open options at refinement time; a single round-trip call would also
  have satisfied every acceptance criterion, just without an exercisable TOCTOU/
  concurrency test.

## Alternatives considered

- **Reflection over event DUs** — rejected; dishonest about wire-format divergence
  (`Game_status_changed`'s nested-string encoding).
- **Hand-maintained template registry** — rejected; a second source of truth that drifts.
- **Single round-trip call (validate+append in one RPC)** — viable, but loses the
  confirmation dialog's real canonical preview and makes the concurrency-conflict
  acceptance criterion untestable in the way this task's Expecto suite exercises it.

## References

- `src/Server/Administration.fs` — `eventCodecs`, `canonicalizeCompensatingEvent`,
  `appendCompensatingEventCore`, and the four new `IAdminApi` methods.
- `src/Server/EventStore.fs` — `getDistinctEventTypesForPrefix`, `getMostRecentEventOfType`.
- `src/Server/EventFormatting.fs:382-390` — the `formatEvent` prefix-dispatch idiom mirrored.
- `src/Server/Games.fs:348-366` — the `Game_status_changed` legacy-value canonicalization
  case exercised directly by this task's Expecto test.
- `src/Client/Pages/StreamDetail/` — the "Append corrective event" UI and paper-overlay
  (ADR-0016) confirmation dialog.
- ADR-0002 (event sourcing + CQRS), ADR-0016 (paper overlay), ADR-0030 (request-connection
  concurrency gate).
