---
id: integration-dvbjp
title: The nightly Audible sync creates a Book for every unmatched library ASIN (same create path as the import, then an ordinary observation) and "Import library" becomes a true one-time bootstrap — stamped `audible_library_imported_at` on its first populated run, refused by the API and hidden by the Settings card afterwards
status: todo
type: feature
context: integration
created: 2026-09-18
completed:
depends_on: [integration-dtdbb]
blocks: []
tags: [audible, integration, sync, import, books, settings]
related_adrs: [0082, 0076, 0074, 0078]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [integration-jjvg2, integration-dhctm]
---

## Why
integration-jjvg2 made the nightly Audible progress sync match by ASIN and NEVER create a book, so a
title bought after the first import only enters the library through another "Import library" click
(or the search modal). With priors (ADR-0082) that leaves the bulk-import button doing two jobs —
bootstrap and new-purchase intake — and every later click records priors for titles the user
already listened to. Builder ruling 2026-09-18: the nightly sync creates unmatched books itself and
observes them normally (a new purchase with yesterday's listening is a real listening day, never a
prior), and "Import library" is one-time only. This reverses jjvg2's "never creates a book" rule
deliberately.

## What
1. Extract the per-item create path out of `Api.importAudibleLibraryImpl` — `AddBookRequest` from
   the library item, `addBookToLibraryImpl` (with `SkipDuplicateCheck = true`), the Audnexus
   fallback for description/narrators, the `book_metadata_cache` upsert with `source = "audible"` —
   into one function both callers use. `AudibleSync.fs` compiles before `Api.fs`, so either move
   `addBookToLibraryImpl`'s dependencies into a module compiled before `AudibleSync.fs` or pass the
   create function into `runProgressSync` as a parameter from `Composition.fs` (the job body
   already closes over `Api`-level helpers via `Composition`). One implementation, no copy.
2. `AudibleSync.runProgressSync`: for an unmatched ASIN, create the book through that function,
   then `Observe_reading_progress` via `observationFor item today` — an ordinary observation dated
   to the sync day, never `Record_prior_reading_progress`, never a `getLastPositionHeard` call.
   `AudibleProgressSyncResult` gains `Created: int`; `Unmatched` is removed (or kept at 0 only for
   items that failed to create, reported in `Errors`). `formatResult` and the Settings card's sync
   result line show the created count.
3. One-time import: `importAudibleLibraryImpl` stamps a new setting `audible_library_imported_at`
   (UTC ISO, like `audible_last_sync`) after a successful run with a non-empty library response
   (an empty-but-200 response never stamps — ADR-0068's lesson, as the existing code treats it).
   When the setting is already set, `importAudibleLibrary` returns `Error "Audible library already
   imported on <date> — new purchases arrive with the nightly sync"` without calling Audible.
4. `AudibleSyncStatus` gains `LibraryImportedAt: string option`; the Settings card hides the
   "Import library" button once it is set and shows "Library imported <date> · new purchases arrive
   with the nightly sync" in its place; the "Sync progress now" button stays.
5. Live-instance handoff: the builder's instance imported before this setting existed, so the
   button shows once more after deploy — that single click is integration-dtdbb's legacy repair
   run (priors dated from Audible), and it stamps the setting. Document this in the Settings card
   result text and in the task Outcome; never touch the live database from a worker.
6. Update the `Api.fs` / `Shared.fs` doc comments that call the import "one-time" (they now mean
   it) and the AudibleSync module comment ("this module NEVER creates a book").

## Acceptance criteria
- [ ] `runProgressSync` with a fixture library containing an ASIN no book carries creates a Book (`Format = Audiobook`, `AudibleAsin` linked, metadata cache slice `source = "audible"`) and appends a `Reading_progress_observed` (`kind = 'observation'`, `observed_on` = sync day) for it; the result reports `Created = 1`.
- [ ] The same run issues zero `Record_prior_reading_progress` commands and zero `getLastPositionHeard` calls (counting stubs).
- [ ] A second sync of the same library creates nothing and appends nothing (per-source no-op).
- [ ] A create failure for one item is reported in `Errors` and does not abort the run for the others.
- [ ] `importAudibleLibrary` stamps `audible_library_imported_at` after a populated run and does not stamp it after an empty-but-200 response.
- [ ] `importAudibleLibrary` with `audible_library_imported_at` set returns `Error` naming the date and makes no Audible call.
- [ ] `getAudibleSyncStatus().LibraryImportedAt` round-trips the setting.
- [ ] The import's create path and the sync's create path are one function (grep: `addBookToLibraryImpl` is called from exactly one Audible-item site).
- [ ] `npm run build`, Expecto and Vitest are green.
- [ ] Settings → Audible shows no "Import library" button after a stamped import, and shows the imported-on line instead. [human-eye]

## Notes
- ADR-0082 records the ruling (Consequences); no separate ADR — the sync's create path reuses the
  import's, so no new tier or boundary question (ADR-0043/0045 unchanged).
- Depends on `integration-dtdbb`: the one-time gate must not ship before the legacy repair, or the
  live instance's single remaining click would be spent before the repair exists.
- `integration-sfmxg` (Goodreads removal, in doing/) edits `AudibleSync.fs`/`Api.fs`/Settings
  neighbours; rebase awareness only.
- Job-run plumbing (`ScheduledJobs.tryStartJob`/`JobRunRecorder`, ADR-0078) is unchanged; the sync
  job simply does more per unmatched item. Creating a book inside the job holds the DB lock only
  around the brief command execution, never across the Audnexus/cover HTTP calls (ADR-0028
  discipline `withLock` already follows).
