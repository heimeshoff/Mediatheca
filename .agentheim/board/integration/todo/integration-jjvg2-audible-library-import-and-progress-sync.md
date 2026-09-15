---
id: integration-jjvg2
title: Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice
status: todo
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [integration-dhctm]
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
SeriesPosition; ReleaseDate; Description }`. `percent_complete` is a float 0–100; round half-up to int.
Items with `is_finished = true` and no percent are treated as 100.

**Import** (`Api.fs`, `IMediathecaApi.importAudibleLibrary: unit -> Async<Result<AudibleImportResult,
string>>`, `AudibleImportResult = { Total; Created; AlreadyKnown; ProgressObserved; Errors: string list }`):
for each library item — `BookProjection.findByExternalId (AudibleAsin asin)`; **known** → skip
creation; **new** → `addBookFromAudible`-equivalent creation from the library item's own fields
(no per-title `getProduct` call: the library response already carries the metadata — the ADR-0069
"diff, don't re-enrich" principle), then Audnexus only when description or narrators are empty.
Then, for every item (known or new) with a percent, `Observe_reading_progress { Percent; Position =
Some (Minutes (round (percent/100 × runtime), Some runtime)) when runtime known; Source = Audible;
ObservedOn = today; Finished = is_finished }` — the aggregate decides whether anything is emitted
(same percent → nothing, ADR-0076). Per-item failures are collected in `Errors`, never abort the
run (ADR-0010's fault isolation). A newly created book whose percent is 0 and not finished stays
`Backlog`; a percent > 0 promotes to `InFocus` via the aggregate rule — so a fresh import of a
200-title library lands the in-progress titles In Focus and the rest in Backlog. Titles that are
`is_finished` land `Finished` with `finished_at = today` (the true finish date is unknown; note it).

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
      3 `book_progress` rows only for the two with percent > 0 (the 0 % item emits an observation
      only if the aggregate rule says so — assert exactly what `Books.decide` does for 0 % on a new
      book, and document it); a second import with the same fixture creates nothing and appends
      **zero** events (same-percent no-op).
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
