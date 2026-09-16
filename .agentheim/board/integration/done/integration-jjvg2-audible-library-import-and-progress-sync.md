---
id: integration-jjvg2
title: Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice
status: done
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [integration-dhctm, integration-wmqn3]
blocks: []
tags: [books, audible, import, sync, scheduled-job, reading-progress]
related_adrs: [0074, 0076, 0043, 0050, 0026, 0065]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [integration-n3vqa, integration-k4vqm]
---

## Why

The builder's goal: "progress should be based on Audible". With the adapter and auth file in place
(integration-dhctm), this task makes the library appear and the percent move by itself — the
Steam-playtime-sync equivalent for books. ADR-0076 makes each observed percent an event; this task is
the only Audible-sourced writer of that event.

## What

**Library fetch** (`Audible.fs`): `getLibrary httpClient authFile` — authenticated
`GET /1.0/library?num_results=1000&page={n}&response_groups=product_desc,product_attrs,media,
contributors,series,percent_complete,is_finished,listening_status,order_details&image_sizes=500`,
paging until a page is short; `AudibleLibraryItem = { Asin; Title; Authors; Narrators; RuntimeMinutes;
PercentComplete: float option; IsFinished: bool; PurchaseDate: string option; CoverUrl; SeriesName;
SeriesPosition; ReleaseDate; Description }`. `percent_complete` is a float 0–100; **floor to int, never
round-half-up** — 99.6 % must floor to 99, not round to 100, so a title Audible hasn't itself marked
finished can never auto-Finish via rounding (`Books.decide` refuses to clamp but happily accepts a
source-supplied 100). Items with `is_finished = true` and no percent are treated as 100 (the source's
own explicit signal, not a rounding artifact).

**Import** (`Api.fs`, `IMediathecaApi.importAudibleLibrary: unit -> Async<Result<AudibleImportResult,
string>>`, `AudibleImportResult = { Total; Created; AlreadyKnown; ProgressObserved; Errors: string list }`):
for each library item — `BookProjection.findByExternalId (AudibleAsin asin)`; **known** → skip
creation; **new** → `addBookFromAudible`-equivalent creation from the library item's own fields
(no per-title `getProduct` call: the library response already carries the metadata — the ADR-0069
"diff, don't re-enrich" principle), then Audnexus only when description or narrators are empty.
Then, for every item (known or new) with a percent, `Observe_reading_progress { Percent; Position =
Some (Minutes (round (percent/100 × runtime), Some runtime)) when runtime known; Source = Audible;
ObservedOn = today; Finished = is_finished }` — the aggregate decides whether anything is emitted.
The no-op comparison is **per-source** (this item's Audible percent vs. the book's *last Audible*
observation, not its global current percent, per `books-y9kxy`'s ADR-0076/ADR-0077 rules): a 0 %
observation on a book with no prior Audible observation emits nothing at all (0 equals the per-source
default baseline) and the book stays `Backlog` — this is the definitive answer to this task's own
first acceptance criterion below; do not leave it as an open question in the test. Per-item failures
are collected in `Errors`, never abort the run (ADR-0010's fault isolation). A newly created book
whose percent is 0 and not finished stays `Backlog`; a percent > 0 promotes to `InFocus` via the
aggregate rule — so a fresh import of a 200-title library lands the in-progress titles In Focus and
the rest in Backlog. Titles that are `is_finished` land `Finished`; since this sync always sends
`ObservedOn = today`, the resulting `Book_status_changed (Finished, Some today)` dates `finished_at`
to today (the true finish date is unknown — this is an accepted approximation, not a bug; a later
Goodreads shelf sync for a book also tracked there can re-date it via `Change_status`'s backdating,
see `integration-wmqn3`).

**Scheduled job** (`ScheduledJobs.JobSpec`, registered in `Composition.fs` next to "Steam playtime
sync"): name `"Audible progress sync"`, hour from `audible_sync_hour` (default 05). Body: if no auth
file → `Skipped "no Audible auth file"`; else `getLibrary` and run the observe step above for known
books only (**never creates books** — creation is the explicit import click). Result persisted as
`audible_last_sync` (ISO) + `audible_last_sync_result` (JSON: observed count, errors) for Settings.
An `AuthFileRejected` → persists `audible_last_error` and returns a failed run with that message;
the next run retries normally (a fresh file may have been pasted).

**Settings**: a **Data Imports** card "Audible library" (next to Steam Family) with "Import library"
(disabled without an auth file), the last import result, last sync time/result, and a "Sync progress
now" button (`IMediathecaApi.runAudibleProgressSync`, which triggers the same job body through
`ScheduledJobs.tryStartJob` so it is recorded as a `manual` job run, ADR-0026). The Jobs admin
section lists the job automatically.

## Acceptance criteria

- [ ] `AudibleLibrarySyncTests.fs` (stubbed handler, `TestDb`): an import over a 3-item fixture
      (one 0 %, one 42 %, one finished) creates 3 books — statuses Backlog / InFocus / Finished — and
      2 `book_progress` rows, only for the two items with percent > 0 (a 0 % observation on a book
      with no prior Audible observation is the per-source default baseline and emits nothing — see
      `books-y9kxy`'s Notes); a second import with the same fixture creates nothing and appends
      **zero** events (same-percent-per-source no-op).
- [ ] Changing the fixture's 42 % to 55 % and running the **job** appends exactly one
      `Reading_progress_observed` (55, Audible, today) and no status change (already InFocus).
- [ ] The job never creates a book: a library item with an unknown ASIN is counted in the result
      as `Unmatched` and no `Book_added_to_library` is appended.
- [ ] Paging: a fixture returning 1000 + 3 items makes two requests with `page=1` and `page=2`.
- [ ] A 401 after the single refresh-and-retry makes the job run end with `audible_last_error`
      set (prefix `"audible auth file rejected: "`) and status failed; with no auth file it ends
      `Skipped`.
- [ ] The job appears in `getScheduledJobs`/Jobs section with name "Audible progress sync" and a
      "Run now" records a `manual` job run.
- [ ] Settings shows the import result counts and the last sync time after a reload (persisted
      via `SettingsStore`, not in-memory).
- [ ] `npm test`, `npm run test:client` green; `npm run build` succeeds.

## Notes

- ADR-0074 (auth file, no login, standing notice), ADR-0076 (observation semantics — the sync sends
  raw observations, the aggregate owns promotion/finish), ADR-0050 (source-tagged engagement
  events), ADR-0026 (job runs), ADR-0069 (diff before enrich — no per-title fetch for known titles),
  ADR-0068's lesson (an empty library response is *inconclusive* — do not clear `audible_last_error`
  or report success on `[]`; report "0 items" plainly).
- Research §2 lists the `/1.0/library` response groups (note: `last_position_heard` is **not** a
  library response group — position comes from `percent_complete × runtime`), §4 the export
  alternative (not built here).
- `ObservedOn` = the server's local date via the same gaming-day-style offset? **No** — keep it the
  plain local calendar date of the sync run; there is no session boundary to protect, unlike play
  minutes.
- Steam's `PlaytimeTracker.fs` is the closest existing sync body; `SteamFamilyIncrementalImportTests`
  the closest test shape.
- **Scheduling note (added during refinement, 2026-09-16):** this task now `depends_on`
  `integration-wmqn3` in addition to `integration-dhctm` — both this task's "Audible progress sync"
  job and `wmqn3`'s "Goodreads shelf sync" job append a new entry to the same `Composition.fs`
  `scheduledJobs` list literal (~line 395); dispatching both jobs' registration in the same parallel
  batch risks a same-line-region merge conflict at squash time. This task and `wmqn3` don't otherwise
  interact — this dependency exists purely to serialize the two `Composition.fs` list-append edits.
- A removed book cannot be re-added under the same slug (see `books-y9kxy`'s Notes); a library title
  the user has removed from Mediatheca will reappear under a new slug on the next import/sync that
  still sees it in the Audible library. This is existing Steam-import behavior; note it as a known
  limitation in your RESULT rather than treating it as a bug to fix here.

## Verifier note (iteration 1)

VERDICT: FAIL (iteration 1 of 3). Runner green (build ok, Expecto 880/0, Vitest 100) — not the reason.

REASONS:
- ADR-0065 as amended by ADR-0068 (bound by this task's own Notes: "do not clear `audible_last_error` or report success on `[]`") is violated: both new code paths clear the standing notice unconditionally on ANY `Ok items`, including an empty library — `Api.fs:1997` (`SettingsStore.deleteSetting conn "audible_last_error"` after the import loop, reached when `items = []`) and `AudibleSync.fs:153` (same call in `runProgressSync`'s final `withLock` block). The guarded house pattern is `Api.fs:598-618`, where `Ok []` is NOT allowed to clear `steam_api_key_last_error` and only a populated `Ok games` does (`integration-k4vqm`, this task's prior art). A standing "audible auth file rejected: …" notice would be silently wiped by a later inconclusive empty-but-200 response. No test exercises an empty library response.
- The import path derives its observation date in UTC while the sync path (and the Notes: "the plain local calendar date of the sync run") uses local: `Api.fs:1917` `DateTime.UtcNow.ToString("yyyy-MM-dd")` vs `AudibleSync.fs:129` `DateTime.Now.ToString("yyyy-MM-dd")`. On a UTC+1/+2 server an import between local midnight and 02:00 stamps yesterday's date (and the `Book_status_changed (Finished, Some today)` date, ADR-0077), so import-then-sync in that window is no longer the same-date no-op the criteria assume. `AudibleLibrarySyncTests.fs` asserts `ObservedOn` against `DateTime.Now` only on the sync path.

SUGGESTED_FIX: Guard both notice-clears on a genuinely populated result — clear `audible_last_error` only when `items` is non-empty (mirroring `Api.fs:598-618`'s `Ok []` vs `Ok games` split), let an empty library report "0 items" plainly without touching the notice, and add a test for an empty-library response asserting a pre-set `audible_last_error` survives. Make `importAudibleLibraryImpl`'s `today` the local calendar date (`DateTime.Now.ToString("yyyy-MM-dd")`) so import and sync stamp the same day, and assert `ObservedOn` on the import path too.

ITERATION_HINT: likely-fixable

## Outcome

Implemented the Audible library import and daily listening-progress sync (ADR-0074/ADR-0076/ADR-0026):

- **`src/Server/Audible.fs`**: `AudibleLibraryItem` + `getLibrary` — authenticated, paged `GET /1.0/library` (`num_results=1000`, `page=n`), paging until a page returns fewer than 1000 items; one logical fetch for `withAccessToken`'s retry-once orchestration (a 401 on any page reports `Unauthorized` for the whole paged fetch).
- **`src/Server/AudibleSync.fs`** (new, compiled before `Api.fs`/`ScheduledJobs.fs`, same shape as `GoodreadsSync.fs`): `percentOf` (floors the source's float percent — 99.6% never rounds to 100 — and treats `is_finished` with no percent as 100) and `observationFor` — the ONE pure decision that both the import and the scheduled job funnel through to build `Observe_reading_progress`. `runProgressSync` re-observes KNOWN books only (matched by `AudibleAsin`) and never creates one; an unmatched ASIN is counted `Unmatched`. A rejected auth file persists `audible_last_error` (prefix `audible auth file rejected: `) and returns an `Error` the caller must surface as a genuine failure.
- **`src/Server/Api.fs`**: `importAudibleLibrary` (new private `executeBookCommandWithEvents` to detect no-op vs. real observations; creates a book per unmatched ASIN directly from the library item's own fields — no per-title `getProduct` call — Audnexus filling description/narrators only when thin) plus `runAudibleProgressSync`/`getAudibleSyncStatus` on `IMediathecaApi`; `Api.create` gained a `runAudibleProgressSyncNow` parameter (mirroring `runGoodreadsShelfSyncNow`), updated at every call site.
- **`src/Server/Composition.fs`**: `audibleSyncHour` setting (default 05:00 local), the "Audible progress sync" `JobSpec` appended after "Goodreads shelf sync" in the shared `scheduledJobs` list, and `runAudibleProgressSyncNow` — the ADR-0026/ADR-0078 wrapper-JobSpec pattern, with one addition: a rejected auth file `failwith`s (after the typed result is captured in `resultCell`) so `tryStartJob` resolves the run to `error`, never `Skipped` — distinct from the plain "no auth file configured" `Skipped` case every other job uses.
- **`src/Shared/Shared.fs`**: `AudibleImportResult`, `AudibleProgressSyncResult`, `AudibleSyncStatus` (kept separate from `AudibleStatus`, mirroring how `GoodreadsSettings` keeps its own LastSync/LastResult) and the three new `IMediathecaApi` members, all appended at the tail after Goodreads per the task's own merge-conflict-avoidance notes.
- **Client** (`src/Client/Pages/Settings/{Types,State,Views}.fs`): an "Import library" / "Sync progress now" section in the Audible card (rendered once an auth file is configured), session-fresh success/error alerts, and the persisted last-import/last-sync summary from `getAudibleSyncStatus` — same Save/Test/Sync-now shape the Goodreads card established. `src/Client/Pages/Settings/AudibleImportSync.test.fs` (new) covers the reducer.
- **Tests**: `tests/Server.Tests/AudibleLibrarySyncTests.fs` (new, iteration 1: 9 cases) covers the full acceptance-criteria list — the 3-item (0%/42%/finished) import creating 3 books with 2 progress observations and a fully idempotent re-run; the job re-observing a 42%→55% change as exactly one event with no extra status change; the job never creating a book (Unmatched counting); paging (1000 + 3 items, two requests, `page=1`/`page=2`); no-auth-file `Skipped` vs. a rejected-auth-file `error` job status (plus the job-registration "Run now" case); and `getAudibleSyncStatus` reading persisted, not in-memory, state. `Api.create`'s new parameter required updating 14 existing test call sites with a matching stub.

**Iteration 2** fixed the two defects the verifier reported against iteration 1:

- **Empty-library response no longer clears `audible_last_error`** (ADR-0068's lesson, bound by this task's own Notes): both `Api.fs`'s `importAudibleLibraryImpl` and `AudibleSync.runProgressSync` now guard their `SettingsStore.deleteSetting conn "audible_last_error"` call on `not (List.isEmpty items)`, mirroring `Api.fs:598-618`'s Steam `Ok []` vs `Ok games` split — an empty (200, zero-item) library response still reports "0 items" plainly (`Total`/`Observed`/`Unmatched` all correctly 0) but leaves a pre-set standing notice untouched. Two new Expecto cases assert this directly: one for the import path, one for the sync path, each seeding `audible_last_error` before the empty-response call and asserting it survives verbatim.
- **`importAudibleLibraryImpl`'s `today` is now the local calendar date**: changed `System.DateTime.UtcNow.ToString("yyyy-MM-dd")` to `System.DateTime.Now.ToString("yyyy-MM-dd")` so the import path stamps `ObservedOn` (and any same-run `Book_status_changed Finished` date, ADR-0077) with the same date `AudibleSync.runProgressSync` already used — an import and a same-day sync are once again the same-date no-op the acceptance criteria assume. A new Expecto case imports a single-item fixture and asserts the resulting `Reading_progress_observed` event's `ObservedOn` against `DateTime.Now.ToString("yyyy-MM-dd")`.
- 3 new tests total (2 in `importAudibleLibraryTests`, 1 in `audibleProgressSyncJobTests`); `npm test`: 883/883 Expecto tests passing (up from 880). `npm run test:client`: 100/100 Vitest tests passing (unchanged). `npm run build`: clean.

**Known limitation (per the task's own Notes, not fixed here):** a book removed from Mediatheca that Audible still reports in the library will reappear under a NEW slug on the next import/sync — the same pre-existing behavior Steam import already has, inherited here via the shared `BookProjection.findByExternalId` lookup.

No new ADR: this task applies the already-decided ADR-0026/ADR-0068/ADR-0074/ADR-0076/ADR-0078 patterns to a new adapter rather than making a new architectural decision; the notice-clearing guard and the local-date fix are both direct applications of already-accepted doctrine, recorded in the README delta and in code comments rather than a standalone ADR.
