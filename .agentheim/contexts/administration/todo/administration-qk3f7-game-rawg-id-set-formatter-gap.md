---
id: administration-qk3f7
title: Add a formatEvent case for Game_rawg_id_set — the one real handled-but-unformattable drift the unknown-event report caught
status: todo
type: bug
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [admin-console, health, integrity, drift]
related_adrs: [0002, 0022]
related_research: []
prior_art: [administration-gxd6e]
---

## Why
Building administration-gxd6e's unknown-event report (Health tab: unhandled /
unformattable event types) surfaced a genuine, currently-existing gap:
`Games.Serialization.handledEventTypes` lists `Game_rawg_id_set` (the
deserializer recognizes it — `Games.fs:555`), but
`EventFormatting.formatGameEvent` has no match arm for it and falls through to
`_ -> None`. Consequences today:

- Every stream drill-in timeline entry for this event type renders as raw JSON
  marked "unformatted" instead of a readable label (the drill-in timeline is
  formatted server-side via `EventFormatting.formatEvent`, ADR-0022).
- It surfaces on the Health tab's unformattable-event-types list (administration-gxd6e).

**This is the *only* such gap.** During refinement every BC's
`handledEventTypes` registry was diffed against its formatter's match arms
(Movies / Series / Games / Friends / Catalogs / ContentBlocks): all six are in
sync **except** Games, where `Game_rawg_id_set` is the single handled type with
no formatter case. Once this lands, `handled ⟺ formattable` holds for every
real event type in the store.

## What
Add a `"Game_rawg_id_set" ->` arm to `EventFormatting.formatGameEvent`
(`src/Server/EventFormatting.fs`), in the same shape as the sibling
`Game_steam_app_id_set` arm. The payload (see `Games.fs:432-435`) is
`rawgId: int` (required) and `rawgRating: float option` (optional):

```fsharp
| "Game_rawg_id_set" ->
    let rawgId = tryFieldInt "rawgId" data |> Option.map string |> Option.defaultValue "?"
    let rating = tryFieldOptionalFloat "rawgRating" data
    let details = [ $"RAWG ID: {rawgId}" ] @ (match rating with Some r -> [ $"Rating: {r}" ] | None -> [])
    Some { Timestamp = ts; Label = "RAWG ID set"; Details = details }
```

Exact label/detail wording is the worker's call — the acceptance criteria fix
the substance (a `Some` with a label plus the RAWG id, and the rating when present),
not the prose.

## The regression test this fix breaks (and how to update it)
`AdministrationTests.fs:397` — `getHealthStats unformattable list flags a
handled event type with no formatter case, independent of the unhandled
check` — deliberately uses `Game_rawg_id_set` as a **real** example of "handled
but unformattable." This fix makes that test's core assertion
(`Expect.isNonEmpty ... UnformattableEventTypes ... "Game_rawg_id_set"`) false,
so it must be updated in the same change.

The task originally hedged "swap to a different genuine gap if one exists" —
**refinement determined none exists** (the diff above). So do **not** pivot to
another real drift case, and do **not** manufacture a fake registry entry (the
`handledEventTypesByBoundedContext` predicates are `private` and built from the
real public `Serialization.handledEventTypes` lists — there is no public seam to
inject a synthetic handled-yet-unformattable type). Instead **repurpose the
test into the positive regression guard for this fix**: append a
`Game_rawg_id_set` sample and assert it now appears in **neither** the
unhandled **nor** the unformattable list (i.e. it is both handled and
formattable) — the Games-BC parallel of the existing `Movie_added_to_library`
"appears in neither list" test at `AdministrationTests.fs:382`. Update the
test's name and its now-stale explanatory comment to reflect that the drift is
closed and the invariant is now `handled ⟺ formattable`. The
independence-of-the-two-checks property remains structurally true
(`buildUnknownEventReport` computes them from separate inputs) and is still
covered on the unhandled side by the existing fabricated-unknown-type test; it
simply can no longer be demonstrated through real data now that the codebase is
fully in sync — that is the intended end state, not a coverage regression.

## Acceptance criteria
- [ ] `EventFormatting.formatEvent` returns `Some` for a `Game_rawg_id_set`
      stored event (a `Game-`-prefixed stream), with a label and a detail
      reflecting the RAWG id, plus the RAWG rating when the payload carries one.
- [ ] The `AdministrationTests.fs:397` test no longer asserts `Game_rawg_id_set`
      is unformattable; it instead asserts `Game_rawg_id_set` appears in
      **neither** `stats.UnhandledEventTypes` nor `stats.UnformattableEventTypes`
      (handled and formattable), and its name/comment are updated to say the
      drift is closed.
- [ ] `npm run build` (Fable compile / type-check) succeeds and `npm test`
      (Expecto) is green.

## Notes
- Sibling arm to copy: `Game_steam_app_id_set` (`EventFormatting.fs:268`) —
  single-id label + detail. `Game_hltb_hours_set` (`:233`) shows the optional-
  field pattern for the rating.
- No client change: the drill-in timeline formats server-side, so a rebuild is
  not required to see the improvement in the UI — only new appends and the
  Health report reflect it, plus any existing `Game_rawg_id_set` rows on next render.
- Not a frontend task (server F# only), so the design-system styleguide gate
  does not apply.
