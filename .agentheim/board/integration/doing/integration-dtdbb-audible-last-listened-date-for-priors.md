---
id: integration-dtdbb
title: Audible priors carry the last-listened day — import and nightly sync fetch `last_position_heard` (`GET /1.0/content/{asin}/metadata`) only for books with no Audible progress row yet, date the prior (and so the finished date) to Audible's `last_updated`, use `position_ms` for the position, and "Import library" repairs the import-day observations written before priors existed
status: doing
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
