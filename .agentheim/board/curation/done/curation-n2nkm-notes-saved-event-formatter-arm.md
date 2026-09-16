---
id: curation-n2nkm
title: "`Notes_saved` is unformattable — `EventFormatting.formatEvent` dispatches by stream prefix and has no `Notes-` arm, so every Notes stream shows in the Health tab's Unformattable list and renders nothing in the event browser; add `formatNotesEvent` (block count + first text excerpt) and the guard test"
status: done
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

## Verifier note (iteration 1)

**REASONS:**
- Acceptance criterion 2 is unmet in production code: `formatNotesEvent` (`src/Server/EventFormatting.fs:447-461`) emits only `Details = [ $"{count} blocks" ]`. The criterion requires `Some` with the label "Notes saved", the block count, **the owner media type and slug**, and **the first text excerpt**, plus **"(empty document)"** for an all-empty document. None of the excerpt, empty-document wording, or owner-in-details is implemented — `Notes.parseStreamId` (which the task's `## Notes` explicitly says to reuse) is never called from `EventFormatting.fs`, and the string `"(empty document)"` appears nowhere in `src/Server/` or `tests/`.
- Acceptance criterion 2 also has no test coverage at all: the diff adds no case to `tests/Server.Tests/EventFormattingTests.fs` (or any formatting test file), which the task's `## What` third bullet names explicitly ("one case for the excerpt/count details and one for the empty-document wording"). The only test added is the Health-tab guard in `AdministrationTests.fs:560-580`, which asserts absence from `UnhandledEventTypes`/`UnformattableEventTypes` and never inspects the formatter's label or details — it would pass against any non-`None` return, including one with empty details.
- The worker's stated justification for dropping the owner from the details misreads ADR-0080. Decision 3 says the owner is not duplicated **into the payload** — it says nothing about the formatter's rendered details, and the criterion's owner comes from the stream id via `Notes.parseStreamId`, which is exactly what ADR-0080 endorses. The deviation is a unilateral narrowing of the spec, not an ADR-mandated one. Regardless, it does not account for the missing excerpt and empty-document behavior, which ADR-0080 has no bearing on.
- Consequently criterion 1 (`Notes_saved` absent from `UnformattableEventTypes`) is the only machine-checkable criterion actually covered; criteria 3 and 4 (suite green) were not evaluated because check 1 fails first.

**SUGGESTED_FIX:** Extend `EventFormatting.formatNotesEvent` to derive the owner via `Notes.parseStreamId storedEvent.StreamId` and add it to `Details` alongside the block count, decode each block's `blockType`/`content` to append the first non-empty text excerpt truncated to ~60 chars, and emit `"(empty document)"` when no block has content; then add the two `formatEvent`-level Expecto cases (excerpt/count details on a `Notes-game-<slug>` stream, and the all-empty-document wording) that the task's `## What` calls for.

**ITERATION_HINT:** likely-fixable

## Outcome

`Notes_saved` events are now formattable in the event browser and no longer appear in the
Health tab's Unformattable list. `EventFormatting.formatNotesEvent`
(`src/Server/EventFormatting.fs`) decodes each block's `blockType`/`content`, then builds
`EventHistoryEntry.Details` from three parts:

- the owner, recovered from the stream id via `Notes.parseStreamId` and rendered as
  `"{Notes.storageToken mediaType} · {slug}"` (e.g. `"game · starcom-unknown-space-2022"`) —
  ADR-0080 decision 3 only says the owner isn't duplicated into the *payload*; it says nothing
  against deriving it from the stream id for display, which is exactly what the task's
  acceptance criteria and `## Notes` call for;
- the block count (`"{n} blocks"`);
- an excerpt: the first block in document order whose `Content` is non-whitespace, trimmed and
  truncated to 60 characters with a trailing "…" if longer, or the literal string
  `"(empty document)"` when no block has non-whitespace content. The rule deliberately doesn't
  gate on `blockType` — an image/columnList/column wrapper never carries non-whitespace
  `Content` itself, so it's naturally skipped without a text/non-text allowlist.

`formatEvent` already dispatched `Notes-` streams to `formatNotesEvent` (added in iteration 1);
that dispatch is untouched.

Tests, all in `tests/Server.Tests/AdministrationTests.fs` (the file already exercising
`EventFormatting` via `getHealthStats`; no separate `EventFormattingTests.fs` existed anywhere in
the suite, so this is genuinely the "nearest existing formatting test file"):

- the pre-existing Health-tab guard (iteration 1): seeds one `Notes_saved` event, asserts it's
  absent from both `UnhandledEventTypes` and `UnformattableEventTypes`;
- new: `EventFormatting.formatEvent` on a `Notes-game-<slug>` stream's `Notes_saved` (a heading
  block with no content plus a text block with a >60-char content string) returns `Some` with
  label `"Notes saved"`, a `"game · starcom-unknown-space-2022"` detail, a `"2 blocks"` detail,
  and a truncated-to-~60-char excerpt detail;
- new: the same call on a `Notes_saved` whose only block has whitespace-only content yields a
  `"(empty document)"` detail.

`dotnet build` on `tests/Server.Tests/Server.Tests.fsproj` is clean (0 warnings, 0 errors).
`npm test` (Expecto) is green: 922/922 passed (919 pre-existing + 3 for this task across both
iterations). `npm run build` was not run — this is a server-only change with no client-facing
type or contract change.
