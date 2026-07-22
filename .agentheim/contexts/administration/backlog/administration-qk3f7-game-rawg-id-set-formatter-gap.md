---
id: administration-qk3f7
title: Add a formatEvent case for Game_rawg_id_set — a real drift the unknown-event report caught
status: backlog
type: fix
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [admin-console, health, integrity, drift]
related_adrs: [0002]
related_research: []
prior_art: [administration-gxd6e]
---

## Why
Building administration-gxd6e's unknown-event report (Health tab: unhandled/
unformattable event types) surfaced a genuine, currently-existing gap:
`Games.Serialization.handledEventTypes` lists `Game_rawg_id_set` (the
deserializer recognizes it — `Games.fs:555`), but
`EventFormatting.formatGameEvent` has no match arm for it and falls through
to `_ -> None`. Every stream drill-in timeline entry for this event type
currently renders as raw JSON marked "unformatted" instead of a readable
label, and it now surfaces on the Health tab's unformattable-event-types
list.

## What
Add a `"Game_rawg_id_set" ->` case to `EventFormatting.formatGameEvent`
(`src/Server/EventFormatting.fs`), following the same shape as the other
Game_* cases — a short label plus the rawgId (and rawgRating, if present) as
details.

## Acceptance criteria
- [ ] `formatEvent` returns `Some` for a `Game_rawg_id_set` stored event, with
      a label and details reflecting the RAWG id (and rating, if present).
- [ ] `Game_rawg_id_set` no longer appears in the Health tab's unformattable-
      event-types list once this lands (the existing regression test in
      `AdministrationTests.fs` that currently asserts it DOES appear will
      need updating to a different real or synthetic drift case, or to
      assert the list no longer contains it).

## Notes
Found by administration-gxd6e's `getHealthStats unformattable list flags a
handled event type with no formatter case...` test — that test currently
relies on this exact gap as a real, deterministic example of "handled but
unformattable." Fixing this closes the drift but will need that test
updated in the same change (swap to a different genuine gap if one exists,
or a locally-constructed StoredEvent exercised directly against
`EventFormatting.formatEvent`/a stub registry, if no real gap remains).
