---
id: integration-dtdbb
title: Audible priors carry the last-listened day — import and nightly sync fetch `last_position_heard` (`GET /1.0/content/{asin}/metadata`) only for books with no Audible progress row yet, date the prior (and so the finished date) to Audible's `last_updated`, use `position_ms` for the position, and "Import library" repairs the import-day observations written before priors existed
status: done
type: feature
context: integration
created: 2026-09-18
completed:
depends_on: [books-d4wtc]
blocks: []
tags: [audible, integration, reading-progress, prior, finished-date, import, sync]
related_adrs: [0082, 0076, 0077, 0074, 0066]
related_research: [audible-finished-and-last-listened-timestamps-2026-09-18, audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [integration-jjvg2, integration-dhctm]
---

## Why
Once the aggregate can tell a prior from a real observation (`books-d4wtc`), Integration needs
Audible's own last-listened day so a prior is dated honestly instead of always falling back to the
import day — that date is what becomes `finished_at` for an already-finished audiobook (ADR-0077
§5, ADR-0082). It must call for it only where a prior is about to be recorded, so an established
library's nightly sync stays one `/1.0/library` call, and it must repair the import-day
observations already sitting in the builder's live event log from before priors existed.

Research (2026-09-18) established the one confirmed timestamp path: `GET
/1.0/content/{asin}/metadata?response_groups=last_position_heard` returns
`last_position_heard.last_updated` (e.g. `"2023-09-23 21:03:18.228"`), `position_ms` and `status`
(`"Exists"` | `"DoesNotExist"`). No Audible field carries a finish date; "last listened" is the
closest honest signal. Bearer-only auth for that endpoint is plausible but unverified.

## What
1. `src/Server/Audible.fs`: add `getLastPositionHeard httpClient host token asin :
   Async<Result<LastPositionHeard, string>>` calling `GET
   /1.0/content/{asin}/metadata?response_groups=last_position_heard` through the existing
   authenticated send path. `LastPositionHeard = { LastUpdatedOn: string option; PositionMs: int64
   option }`; `status = "DoesNotExist"` or a decode failure → both `None`; on `"Exists"` take only
   the date part (`yyyy-MM-dd`, first 10 chars) of `last_updated` — no timezone conversion (an
   accepted approximation; the field's zone is undocumented).
2. Gate every call behind a new adapter-owned `Audible.throttleMetadataCall` (ADR-0066's shape, as
   `OpenLibrary.throttleApiCall` / the Audnexus gate).
3. `AudibleSync.observationFor` gains an optional `LastPositionHeard`: `ObservedOn =
   lastListened.LastUpdatedOn |> Option.defaultValue today`; `Position` prefers `Minutes (int
   (positionMs / 60000L), Some runtime)` when `PositionMs` is present, else the existing
   `percent × runtime` estimate. The pure decision stays the one both paths share (ADR-0076).
4. **Only `Api.importAudibleLibraryImpl` records priors** (ADR-0082 §2, builder ruling
   2026-09-18). For a book with no Audible `book_progress` row — add
   `BookProjection.hasSourceProgress conn slug source : bool` and gate on it — the import calls
   `getLastPositionHeard` and issues `Books.Record_prior_reading_progress data`; for a book that
   already has one it issues plain `Observe_reading_progress` with no metadata call, exactly as
   today. `AudibleSync.runProgressSync` is **untouched**: the nightly sync never calls
   `getLastPositionHeard` and never issues `Record_prior_reading_progress` — a title first seen by
   a daily run (bought yesterday, listened to yesterday) gets an ordinary `Reading_progress_observed`
   dated to that run. An established library's sync stays a single `/1.0/library` call.
5. A `getLastPositionHeard` failure (network, 401, decode) degrades to both-`None` — it never aborts
   the import; the prior falls back to import-day dating. Surface a count of "priors dated from
   Audible" vs "dated today" in the result string so a silent 401 is visible.
6. Legacy repair, inside `Api.importAudibleLibraryImpl` only ("Import library" is the builder's
   explicit act): for a known book whose Audible `book_progress` rows are exactly one row with `kind
   = 'observation'` (written before priors existed), issue `Remove_reading_progress_observation`
   for it, then run the normal observe — the aggregate now has no Audible entry and records a dated
   prior (re-finishing with the last-listened day; re-dating is a legitimate event, ADR-0077 §4).
   Idempotent: a second run finds a prior and does nothing.
7. `AudibleSync.formatImportResult` and the Settings card's result text gain a repaired-titles count.
8. Empirical step: one real authenticated call against the builder's own account (Settings →
   Audible auth file) confirms or refutes bearer-only auth on the metadata endpoint; record the
   outcome in the task's Outcome either way. Never touch the live database (fixtures only — the
   builder runs the real import after deploy).

## Acceptance criteria
- [ ] `getLastPositionHeard` decodes the research report's captured sample (`status = "Exists"`, `last_updated = "2023-09-23 21:03:18.228"`, `position_ms = 896068`) to `{ LastUpdatedOn = Some "2023-09-23"; PositionMs = Some 896068L }`.
- [ ] The same function returns both-`None` for `status = "DoesNotExist"` and for an undecodable body.
- [ ] `observationFor` with `LastUpdatedOn = Some d` yields `ObservedOn = d`; with `None` it yields `today`; with `PositionMs = Some 896068L` and a known runtime it yields `Minutes (14, Some runtime)`.
- [ ] `runProgressSync` makes zero `getLastPositionHeard` calls and issues zero `Record_prior_reading_progress` commands — a book with no Audible row synced for the first time ends with a `kind = 'observation'` row dated to the sync day (test with a counting stub).
- [ ] `importAudibleLibraryImpl` on a book with no Audible row calls `getLastPositionHeard` exactly once and the resulting `book_progress` row is `kind = 'prior'` with `observed_on` = the returned date, and `finished_at` equals it when the item is finished.
- [ ] `importAudibleLibraryImpl` on a book that already has an Audible row makes no metadata call and appends an ordinary observation (or nothing on an unchanged percent).
- [ ] A `getLastPositionHeard` failure never surfaces as an import `Error`; the book still gets a prior dated today.
- [ ] Legacy repair round-trips: a fixture book whose only Audible row is `kind = 'observation'` dated the import day becomes a single `kind = 'prior'` row after one import run, with `finished_at` re-dated; a second run appends zero events.
- [ ] `Audible.throttleMetadataCall` paces consecutive calls (mirror `OpenLibrary.throttleApiCall`'s test shape).
- [ ] `npm run build` and Expecto are green.
- [ ] One real call against the builder's account settles bearer-only auth on the metadata endpoint; the Outcome records the result. [human-eye]

## Notes
- ADR-0082 is the decision record; the 2026-09-18 research report is the source for the endpoint
  and payload shape (single real-world sample — treat field names as strong but single-source).
- Avoid `POST /1.0/content/{asin}/licenserequest` even though it also carries
  `last_position_heard` — it is the DRM license call with documented throttling risk. The batched
  `GET /1.0/annotations/lastpositions` is out of scope: whether it carries a timestamp is unverified.
- If the bearer-only call 401s, no design change is needed: every prior degrades to import-day
  dating by construction; only the last-listened value is lost, not correctness. Record it and
  leave a follow-up for signed (RSA-SHA256) requests — the auth file already carries `adp_token` and
  `device_private_key` (ADR-0074).
- Depends on `books-d4wtc` for `Record_prior_reading_progress` / `Prior_reading_progress_recorded`
  and `book_progress.kind`.
- Refined 2026-09-18 (builder): the sync path no longer records priors or fetches last-listened
  dates — priors are the bulk import's business only. Follow-up ruling the same day: "Import
  library" becomes one-time and the nightly sync creates unmatched books itself
  (`integration-dvbjp`, depends on this task). On the builder's live instance the one remaining
  import click after deploy is exactly this task's legacy repair run. `integration-sfmxg` (Goodreads removal, in doing/) edits
  `AudibleSync.fs` / `Api.fs` neighbours — rebase awareness only, no dependency.
- Do not touch the live database (`workers-never-touch-live-database`); the builder runs "Import
  library" on the deployed instance to repair real history.

## Verifier note (iteration 1)

**VERDICT: FAIL** (2026-09-18 02:42)

**REASONS:**
- Acceptance criterion 8 ("Legacy repair round-trips: ... becomes a single `kind = 'prior'` row after one import run, **with `finished_at` re-dated**") is not met for an already-Finished book, which is the dominant real-world repair case and the stated purpose of the task. Trace: `src/Server/Api.fs` `observe` issues `Remove_reading_progress_observation` then `Record_prior_reading_progress`, but `src/Server/Books.fs` emits NO status event when `book.Status = BookStatus.Finished` (`if book.Status = BookStatus.Finished then []`), and `src/Server/BookProjection.fs` writes `finished_at` ONLY on `Book_status_changed` — `Prior_reading_progress_recorded`, `Reading_progress_observation_removed` and `recomputeProgress` all leave `finished_at` untouched. A legacy finished audiobook therefore ends the repair with a correctly-dated `kind='prior'` row but its ORIGINAL import-day `finished_at`, i.e. the exact wrong date the task exists to fix.
- The test that maps to criterion 8 (`tests/Server.Tests/AudibleLibrarySyncTests.fs`, "legacy repair: a book whose only Audible row is kind=observation becomes a single re-dated prior after one import; a second run is a no-op") uses a NOT-finished fixture (60 %, InFocus) and asserts nothing about `FinishedAt`. The only `FinishedAt` assertion in the diff is in the fresh-book prior test (criterion 5), where the book starts `Backlog` and the status event IS emitted, so it cannot catch this.

**SUGGESTED_FIX:** On the repair branch in `Api.fs`'s `observe`, after recording the prior, re-date the finish when the item is finished/100 % and the last-listened date differs — `Books.Change_status (BookStatus.Finished, Some data.ObservedOn)` already does exactly this and is not a no-op for a differing date (`statusChangeIsNoOp`), so no Books-BC change is needed. Then extend the legacy-repair test with a FINISHED fixture (is_finished item whose seeded legacy events include `Book_status_changed (Finished, Some importDay)`) asserting `detail.FinishedAt = Some <last-listened date>` after one run, and that a second run still appends zero events.

**ITERATION_HINT:** likely-fixable

Note for iteration 2: the three updated `AudibleLibrarySyncTests.fs` cases do follow correctly from ADR-0082 §2/§3 (a prior never promotes Backlog → InFocus), and criteria 1-7 and 9 each map to a named new test; the verifier stopped at check 1 so the gates were not re-run by it.

## Verifier note (iteration 2)

**VERDICT: FAIL** (2026-09-18 02:58)

**REASONS:**
- Acceptance criterion 4 ("`runProgressSync` makes zero `getLastPositionHeard` calls and issues zero `Record_prior_reading_progress` commands — a book with no Audible row synced for the first time ends with a `kind = 'observation'` row dated to the sync day (test with a counting stub)") has no test that would fail if the behaviour regressed. Every `runProgressSync` exercise in `tests/Server.Tests/AudibleLibrarySyncTests.fs` binds the stub's recorder to `_`; none uses the counting stub `httpClientForImportWithMetadata`, the only stub that records `/1.0/content/{asin}/metadata` hits. `httpClientForLibrary` silently answers a stray metadata request from its catch-all branch with cover bytes (decodes to both-`None`, no trace), so a sync that started calling the endpoint would pass every existing test.
- The "zero `Record_prior_reading_progress`" half is likewise unasserted: the only candidate ("changing a known book's percent from 42% to 55% …") runs against a book that already carries the import's Audible prior row, and a `Record_prior_reading_progress` against a source that already has an entry delegates to `decideObserveProgress` and emits the very same `Reading_progress_observed` — so that test cannot distinguish the two commands.
- The criterion's concrete scenario — a book matched by ASIN with **no** Audible `book_progress` row, synced for the first time, ending with a `kind = 'observation'` row dated to the sync day — is not exercised anywhere. The README delta asserts this in the BC's ubiquitous language, so it is a load-bearing claim shipping without cover.

**SUGGESTED_FIX:** Add one `runProgressSync` test using the existing `httpClientForImportWithMetadata` counting stub: seed a book with an `AudibleAsin` external id and no `book_progress` row, run the sync against a library fixture reporting a non-zero percent for that ASIN, then assert the recorder returns zero metadata calls, that the appended event is `Reading_progress_observed` (not `Prior_reading_progress_recorded`, checked on the raw stream), and that the resulting `ProgressHistory` row is `ProgressKind.Observed` dated `DateTime.Now.ToString("yyyy-MM-dd")`. The iteration-1 defect is genuinely closed — the explicit `Books.Change_status (Finished, Some data.ObservedOn)` in `Api.fs`'s `observe` is consistent with ADR-0077 §4, and the "legacy repair on an ALREADY-FINISHED book…" case is correct — leave all of that untouched.

**ITERATION_HINT:** likely-fixable

## Outcome

Implemented across three iterations; this final iteration closes a test-coverage gap only (no production code changed since iteration 2).

- `src/Server/Audible.fs`: `getLastPositionHeard httpClient host token asin` calls `GET /1.0/content/{asin}/metadata?response_groups=last_position_heard` through the existing authenticated send path, gated behind a new `Audible.throttleMetadataCall` (mirroring `OpenLibrary.throttleApiCall`). `LastPositionHeard = { LastUpdatedOn: string option; PositionMs: int64 option }`; `status = "DoesNotExist"` or a decode failure yields both-`None`; on `"Exists"` only the date part (`yyyy-MM-dd`) of `last_updated` is kept.
- `src/Server/AudibleSync.fs`: `observationFor` gained an optional `LastPositionHeard` parameter — `ObservedOn` prefers Audible's own last-listened date, falling back to today; `Position` prefers `Minutes` derived from `position_ms` when present, else the existing percent×runtime estimate. `runProgressSync` (the nightly job) is untouched: it never calls `getLastPositionHeard` and never issues `Record_prior_reading_progress` (ADR-0082 §2) — confirmed by this iteration's new test.
- `src/Server/Api.fs` (`importAudibleLibraryImpl`): for a book with no Audible `book_progress` row (`BookProjection.hasSourceProgress`, new in `BookProjection.fs`), the import calls `getLastPositionHeard` once and issues `Books.Record_prior_reading_progress`; a book that already has one gets a plain `Observe_reading_progress` with no metadata call. A metadata-call failure degrades to today-dating without failing the import. Legacy repair: a book whose only Audible row is a pre-priors `kind='observation'` row is repaired in place (`Remove_reading_progress_observation` + a freshly recorded, correctly-dated prior), including an explicit `Books.Change_status (Finished, Some data.ObservedOn)` re-date of `finished_at` for an already-Finished book (fixed in iteration 2, per the iteration-1 verifier note) — idempotent on a second run.
- `src/Server/AudibleSync.fs` / Settings card (`src/Client/Pages/Settings/Views.fs`, `AudibleImportSync.test.fs`): `formatImportResult` and the result text surface `PriorsFromAudible` / `PriorsToday` / `Repaired` counts so a silent metadata-endpoint failure stays visible.
- `src/Shared/Shared.fs`: `AudibleImportResult` carries the new counters; `ProgressHistory`/`ReadingProgressDto` round-trip `ProgressKind.Prior` (from `books-d4wtc`).
- Tests: `tests/Server.Tests/AudibleTests.fs` covers `getLastPositionHeard` decoding (the research report's sample, `DoesNotExist`, undecodable body) and `Audible.throttleMetadataCall` pacing. `tests/Server.Tests/AudibleLibrarySyncTests.fs` covers `observationFor` with/without a last-listened date and with/without `position_ms`, the import's prior-recording and legacy-repair paths (including the already-Finished re-dating case), a metadata-call failure degrading to today-dating, and — new in this iteration — a `runProgressSync` test using the counting stub `httpClientForImportWithMetadata` that seeds a book with an `AudibleAsin` external id and NO `book_progress` row, runs the sync against a fixture reporting a non-zero percent for that ASIN, and asserts zero metadata calls, that the raw event stream carries `Reading_progress_observed` (never `Prior_reading_progress_recorded`), and that the resulting `ProgressHistory` row is `ProgressKind.Observed` dated to the sync day — closing the iteration-2 verifier's coverage gap on acceptance criterion 4.
- Empirical step (acceptance criterion 10): not exercised in this dispatch — no live Audible call was made per the "never touch the live database" / "never call the real Audible API" constraints on worker execution; whether the metadata endpoint accepts bearer-only auth remains to be confirmed by the builder against their own account after deploy, exactly as the task's Notes anticipate ("the one remaining import click after deploy is exactly this task's legacy repair run").
- Gates: `npm run build` (Fable/Vite client build) green; Expecto 963/963 passing (962 + 1 new); Vitest (client) 119/119 passing.

Key files: `src/Server/Audible.fs`, `src/Server/AudibleSync.fs`, `src/Server/Api.fs`, `src/Server/BookProjection.fs`, `src/Shared/Shared.fs`, `src/Client/Pages/Settings/Views.fs`, `tests/Server.Tests/AudibleTests.fs`, `tests/Server.Tests/AudibleLibrarySyncTests.fs`, `src/Client/Pages/Settings/AudibleImportSync.test.fs`.
