---
id: administration-gxd6e
title: Unknown-event report — distinct event types no projection handler recognizes or formatEvent can't render, with counts and samples
status: done
type: feature
context: administration
created: 2026-07-22
completed: 2026-07-22
depends_on: [design-system-001]
blocks: []
tags: [admin-console, health, integrity, drift]
related_adrs: [0002, 0021]
related_research: []
prior_art: []
---

## Why
Schema drift accumulates silently: event types the code no longer handles
(legacy cases like `"Playing"` folded into `InFocus` — see the comment at
`EventFormatting.fs:43`) or types no BC's decoder recognizes can sit in the log
unnoticed until something reads wrong. This surfaces them on the Health tab as a
"Run check" report so an operator can see them explicitly.

## What
- Add a small, hand-maintained `handledEventTypes: string list` value to each
  BC's `Serialization` module (Movies, Series, Games, Friends, Catalogs,
  ContentBlocks) — literally the string literals already appearing as match-arm
  patterns in each `deserialize` (e.g. `Movies.fs:343-413`). F# pattern matches
  aren't reflectively introspectable, so this can't be derived; it's declared,
  the same way `Administration.boundedContextPrefixes` / `projectionTables` /
  `imageRefColumns` are already hand-maintained "admin-console-only knowledge of
  a BC's shape."
- Register these in `Administration.fs` alongside `boundedContextPrefixes`
  (bounded-context / stream-prefix → handled event types).
- Report query: for each distinct `(eventType, count)` from
  `EventStore.getEventCountsByType` (already an index-only scan per ADR-0021,
  no new query cost):
  - **Unhandled:** the type's owning BC (resolved via stream prefix on a sample
    event) doesn't list it in `handledEventTypes`, or the type's stream prefix
    matches no known BC at all.
  - **Unformattable:** one sample stored event of that type, run through
    `EventFormatting.formatEvent`, returns `None`.
- Both lists rendered on the Health tab with type name, count, and one sample
  event (raw JSON, same rendering as the stream drill-in's raw-JSON toggle).
  Display-only, not persisted.
- Extend `HealthStats` / `IAdminApi.getHealthStats` (or a small sibling method)
  with the two lists — keep the one-round-trip shape ADR-0021 established for
  this tab.

## Acceptance criteria
- [ ] A fabricated unknown event type inserted directly into a test event store
      (bypassing all `Serialization.toEventData` helpers) appears in the
      unhandled list with the correct count.
- [ ] A real, currently-handled event type does **not** appear in either list
      (negative case — guards against a registry entry silently drifting out of
      sync with its `deserialize` match).
- [ ] The formatEvent-unformattable list correctly includes a fabricated type
      even when that type IS present in `handledEventTypes` (the two lists are
      independent checks, not aliases of each other).
- [x] A fabricated unknown event type inserted directly into a test event store
      (bypassing all `Serialization.toEventData` helpers) appears in the
      unhandled list with the correct count.
- [x] A real, currently-handled event type does **not** appear in either list
      (negative case — guards against a registry entry silently drifting out of
      sync with its `deserialize` match).
- [x] The formatEvent-unformattable list correctly includes a fabricated type
      even when that type IS present in `handledEventTypes` (the two lists are
      independent checks, not aliases of each other).
- [x] Health tab renders both lists with count + one sample event's raw JSON,
      consistent with the tab's existing paper-overlay / DaisyUI styling.
      [human-eye]

## Outcome
Added a hand-maintained `handledEventTypes: string list` to each of the six
core BCs' `Serialization` modules (Movies, Series, Games, Friends, Catalogs,
ContentBlocks), registered in `Administration.handledEventTypesByBoundedContext`
alongside `boundedContextPrefixes`. `Administration.buildUnknownEventReport`
runs the two independent checks (unhandled / unformattable) over
`EventStore.getEventCountsByType`, using one new indexed point-lookup helper
(`EventStore.getSampleEventForType`) per distinct event type. `HealthStats`
gained `UnhandledEventTypes`/`UnformattableEventTypes: UnknownEventTypeRow list`
(kept in the existing single-round-trip `getHealthStats` shape per ADR-0021).
The Health tab (`src/Client/Pages/AdminHealth/Views.fs`) renders both lists
with type/count/raw-JSON-sample, matching the stream drill-in's raw-JSON
block styling, with an empty-state message when a check finds nothing.

While writing the acceptance-criterion-3 test, found a genuine, currently-
existing drift case (not fabricated): `Game_rawg_id_set` is handled by
`Games.Serialization.deserialize` but has no case in
`EventFormatting.formatGameEvent` — used directly as the independence-proving
test case rather than a synthetic double, and filed as
`administration-qk3f7` (backlog) to fix the formatter gap itself.

Key files: `src/Server/EventStore.fs` (`getSampleEventForType`),
`src/Server/Administration.fs` (`handledEventTypesByBoundedContext`,
`isHandledByBoundedContext`, `buildUnknownEventReport`, wired into
`buildHealthStats`), `src/Server/{Movies,Series,Games,Friends,Catalogs,ContentBlocks}.fs`
(`Serialization.handledEventTypes`), `src/Shared/Shared.fs`
(`UnknownEventTypeRow`, extended `HealthStats`),
`src/Client/Pages/AdminHealth/Views.fs` (unhandled/unformattable sections),
`tests/Server.Tests/AdministrationTests.fs` (4 new test cases).

## Notes
- **Independent** of `administration-btvqa` (the shadow-table drift detector) —
  no shared code or ordering dependency; both were split from one original task
  (the builder's split-by-feature decision: drift detector → Projections tab,
  this report → Health tab) and can ship in either order. Both still carry
  `design-system-001` in `depends_on` per the BC README's frontend gate.
- The `handledEventTypes` registry is new, load-bearing, hand-maintained state
  (six list literals) — flag in review the same way `imageRefColumns` was
  flagged in ADR-0025: a missed / stale entry under-reports drift rather than
  crashing. The negative-case acceptance criterion above is the enforcement
  guard; a broader coverage test (assert every registry entry actually appears
  as a match arm, or a fixture round-trip per declared type) is worth adding.
- No new ADR expected — this reuses ADR-0021's Health-tab query discipline and
  the existing `boundedContextPrefixes` registry pattern; the declared-types
  list is a data addition, not an architectural decision.
