---
id: curation-n2nkm
title: "`Notes_saved` is unformattable — `EventFormatting.formatEvent` dispatches by stream prefix and has no `Notes-` arm, so every Notes stream shows in the Health tab's Unformattable list and renders nothing in the event browser; add `formatNotesEvent` (block count + first text excerpt) and the guard test"
status: doing
type: bug
context: curation
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [notes, administration, health, event-browser, curation]
related_adrs: [0080]
related_research: []
prior_art: [curation-h98ve, curation-w9fkq]
---

## Why

Seen on harbour on 2026-09-16, right after the live Notes migration: the Health tab's
"Unformattable event types" list carries `Notes_saved` (14 events). `EventFormatting.formatEvent`
dispatches on the stream prefix — `Movie-`, `Series-`, `Game-`, `Book-`, `Friend-`, `Catalog-` —
and has no `Notes-` arm, so every `Notes_saved` event falls through to `None`. curation-h98ve
(Notes server core) added the event type, the codec and `handledEventTypes` but never a formatter,
and no test caught it because the unformattable check only runs over stored events.
curation-w9fkq's verifier flagged the identical gap for `Entry_media_types_inferred` a day earlier
and it was fixed with a formatter arm plus a guard test; this is the same fix for Notes.

Harmless for the app — the events replay and the Notes panels work — but the Health tab stays
flagged and the event browser cannot render a Notes stream's history.

## What

- `src/Server/EventFormatting.fs`: add `formatNotesEvent` and dispatch `Notes-` to it in
  `formatEvent`. For `Notes_saved`: label "Notes saved", details = block count (e.g. "3 blocks")
  plus the first non-empty text `content` excerpt truncated to ~60 characters, or "(empty
  document)" when no block has content. Owner comes from `Notes.parseStreamId` (media type +
  slug) and goes into the details as well, so a stream drill-in reads "Notes saved · game ·
  starcom-unknown-space-2022 · 12 blocks · 'Started the campaign…'".
- `tests/Server.Tests/AdministrationTests.fs`: the same guard shape as the
  `Entry_media_types_inferred` case — seed one `Notes_saved` event on a fixture store, compute
  health stats, assert `Notes_saved` is absent from `UnformattableEventTypes`.
- `tests/Server.Tests/EventFormattingTests.fs` (or the nearest existing formatting test file):
  one case for the excerpt/count details and one for the empty-document wording.

## Acceptance criteria

- [ ] Expecto: a stored `Notes_saved` event no longer appears in `HealthStats.UnformattableEventTypes`.
- [ ] Expecto: `formatEvent` on a `Notes-game-<slug>` stream's `Notes_saved` returns
      `Some` with label "Notes saved", the block count, the owner media type and slug, and the
      first text excerpt; an all-empty document yields "(empty document)".
- [ ] Every other bounded context's formatter output is unchanged (existing formatting tests green).
- [ ] `dotnet build`, `npm test` green; `npm run build` unaffected (server-only change).
- [ ] Health tab on harbour after deploy: `Notes_saved` gone from the Unformattable list. [human-eye]

## Notes

- `Notes.parseStreamId` already exists (`src/Server/Notes.fs`) — reuse it rather than re-parsing
  the prefix in the formatter.
- The Health tab on harbour also lists `Content_block_added/removed/updated` as unhandled and
  unformattable: those are the five leftover events of owners Gate 2 excluded (ambiguous/orphan),
  cleaned up by hand via Surgery's delete-by-global-position — not this task's concern and not a
  formatter gap, since the ContentBlocks context is deliberately gone (curation-j4qqt).
