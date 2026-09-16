---
id: curation-tb0nn
title: Guard Gate 1 / Gate 2 previews after the legacy purge — once `content_blocks`/`game_journal_blocks` are dropped both previews return HTTP 500 ("no such table"); with a `tableExists` check they report "legacy stores already purged" with zero owners/streams and Gate 2's confirm stays disabled
status: todo
type: bug
context: curation
created: 2026-09-16
completed:
depends_on: [design-system-001-formalize-styleguide]
blocks: []
tags: [notes, migration, administration, surgery, curation]
related_adrs: [0080, 0034]
related_research: []
prior_art: [curation-j4qqt]
---

## Why

Observed on the dev copy on 2026-09-16, right after running Gate 2 ("Purge legacy stores"):
both `IAdminApi.previewNotesMigration` and `IAdminApi.previewPurgeLegacyNotes` answer with
HTTP 500 — `Failed to process database command: SELECT DISTINCT movie_slug FROM content_blocks
WHERE session_id IS NULL`. Gate 1's owner resolution (`Administration.getContentBlockOwnerSlugs`
/ `getGameJournalOwnerSlugs`) queries the two legacy tables with no existence guard, and Gate
2's preview calls Gate 1's preview first, so the whole Surgery tab section errors on every click
once the purge has run. The builder's experience was "nothing happened, then the button is
inactive" — the failed preview never reaches the model, so Confirm stays disabled with no
explanation.

The same will happen on harbour between running Gate 2 there and deploying the tooling's
retirement (curation-kezpv). The gates must degrade gracefully to "already purged".

## What

- In `src/Server/Administration.fs`, guard both legacy reads with the existing `tableExists`
  helper (already used by the Health tab's orphan-image and drift checks): a missing table
  contributes zero owner slugs, never an exception.
- Extend `NotesMigrationPreview` (Shared) with a flag such as `LegacyStoresPresent: bool` (false
  when neither `content_blocks` nor `game_journal_blocks` exists), so the client can render the
  post-purge state honestly instead of an empty "0 resolved, 0 ambiguous, 0 orphaned".
- `previewPurgeLegacyNotesCore` reuses the guarded preview: empty default set, empty excluded
  set, zero events, no exception. `runNotesMigrationCore` on a purged store converts zero owners
  and appends nothing.
- `AdminSurgery/Views.fs`: when `LegacyStoresPresent = false`, both gate cards show one line —
  "Legacy stores already purged — nothing to migrate" — and Gate 2's preview/confirm buttons stay
  disabled. Same DesignSystem tokens as the existing gate copy; no new component.

## Acceptance criteria

- [ ] Expecto: on a fixture store where neither legacy table exists, `previewNotesMigration`
      returns zero resolved / ambiguous / orphan owners and `LegacyStoresPresent = false`
      without throwing.
- [ ] Expecto: on the same store, `previewPurgeLegacyNotes` returns an empty default set, an
      empty excluded set and `EventCount = 0` without throwing.
- [ ] Expecto: on the same store, `runNotesMigration` reports `Converted = 0` and appends no
      event.
- [ ] Expecto: the existing `NotesMigrationTests.fs` full-flow case (legacy tables present) is
      unchanged and still green, and its preview reports `LegacyStoresPresent = true`.
- [ ] `dotnet build`, `npm run build`, `npm test`, `npm run test:client` green.
- [ ] Surgery tab on a purged dev copy: both gate cards show the "already purged" line, no
      error toast, Gate 2's buttons disabled. [human-eye]

## Notes

- `tableExists` lives in `Administration.fs` (around the Health-tab orphan-image section); it is
  `private` in the same module, so no visibility change is needed.
- This guard is short-lived by design: curation-kezpv deletes both gates outright once the live
  store on harbour has been migrated. Ship this first so the Surgery tab stays clean on harbour
  in between.
- Known, deliberately unfixed (builder decision 2026-09-16): Gate 1 skips any owner that already
  has a `Notes-*` stream, including an editor-created empty document. On harbour, run Gate 1
  before opening any detail page's Notes tab on the new build. Recorded in curation-kezpv's Notes
  as well.
