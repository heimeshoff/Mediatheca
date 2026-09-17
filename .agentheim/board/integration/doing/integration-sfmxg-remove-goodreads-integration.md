---
id: integration-sfmxg
title: Remove the Goodreads integration — delete the adapter, the shelf/progress sync job, the Settings card and the API surface, and drop the Goodreads progress source and external id from the Book model; Audible and Open Library remain the only book sources
status: doing
type: refactor
context: integration
created: 2026-09-18
completed:
depends_on: [integration-wmqn3, integration-y2ak4, design-system-001-formalize-styleguide]
blocks: []
tags: [books, goodreads, adapter, settings, sync, scheduled-job, removal]
related_adrs: [0075, 0078, 0076, 0077]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
prior_art: [integration-wmqn3, integration-y2ak4]
---

## Why

Builder decision, 2026-09-18: the Goodreads path is not wanted. Audible (library, listening
progress) and Open Library (search, metadata, covers) are the book sources Mediatheca keeps.
The Goodreads shelf/progress sync (integration-wmqn3, integration-y2ak4, ADR-0075 §Goodreads,
ADR-0078) was shipped on 2026-09-16 and never used: as of 2026-09-18 **both** the dev event
store and the live store on harbour hold zero events whose payload mentions Goodreads, zero
`goodreads_*` settings rows, and zero `book_detail.goodreads_book_id` values. That makes this a
clean deletion — no persisted event carries `ProgressSource.Goodreads` or
`ExternalId.GoodreadsBookId`, so the discriminated-union cases can go without any replay or
upcasting concern (ADR-0002/ADR-0043 stay honored: nothing in the log is left undecodable).

Removing it now also removes a Settings card and a scheduled job that would otherwise sit
unconfigured forever, and shrinks the `Api.create` surface every test constructs.

## What

Delete every Goodreads-specific surface and the Goodreads vocabulary from the Book model, in one
task, so the codebase has no notion of Goodreads afterwards. The remaining book sources are
untouched: Audible (integration-dhctm, integration-jjvg2, ADR-0074) and Open Library
(integration-c8d4x, ADR-0075's Open Library / Audnexus half).

**Server (`src/Server/`)**
- Delete `Goodreads.fs` and `GoodreadsSync.fs`; remove both `<Compile Include>` lines from
  `Server.fsproj`.
- `Composition.fs`: remove `getGoodreadsConfig`, `goodreadsSyncHour`, the `"Goodreads shelf
  sync"` `JobSpec`, `runGoodreadsShelfSyncNow`, and the two matching `Api.create` arguments.
  Audible's `runAudibleProgressSyncNow` (which "mirrors `runGoodreadsShelfSyncNow`") stays — it is
  now the sole carrier of the ADR-0078 wrapper-JobSpec pattern; re-home its comments so they no
  longer point at a deleted function.
- `Api.fs`: remove the five `IMediathecaApi` members (`getGoodreadsSettings`,
  `setGoodreadsUserId`, `setGoodreadsImportShelves`, `testGoodreadsConnection`,
  `runGoodreadsShelfSync`), `decode/encodeGoodreadsImportShelves`, and the `GoodreadsBookId`
  attachment at add-from-Open-Library time. Stop reading the `goodreads_user_id`,
  `goodreads_import_shelves`, `goodreads_sync_hour`, `goodreads_last_sync`,
  `goodreads_last_sync_result`, `goodreads_last_error` settings keys — no row with any of them
  exists in dev or live, so no cleanup migration is needed.
- `OpenLibrary.fs`: drop the `GoodreadsIds` field and the `identifiers.goodreads` decode from the
  edition decoder (it existed only to seed the Goodreads external id).
- `Books.fs` / `BookProjection.fs`: remove the `Goodreads` / `GoodreadsBookId` encode-decode
  branches, the `goodreads_book_id` column from the `book_detail` CREATE/INSERT/SELECT and its
  partial UNIQUE index, and the `WHEN 'Goodreads' THEN 2` rung of the effective-progress
  precedence — the rule becomes **Manual > Audible** (amends ADR-0076 §2's ordering by removing a
  rung; the rule itself is unchanged). An existing projection DB that still carries the now-orphan
  nullable column keeps working (INSERTs simply omit it); a projection rebuild (`DROP TABLE` +
  recreate) yields the column-free schema. No migration.
- `AudibleSync.fs`: rewrite the doc comments that cite `GoodreadsSync.withLock` /
  `GoodreadsSync.formatResult` as the sibling — point at `PlaytimeTracker` instead.

**Shared (`src/Shared/Shared.fs`)**
- Remove `ProgressSource.Goodreads` (leaving `Audible | Manual`), `ExternalId.GoodreadsBookId`,
  `BookDetail.GoodreadsBookId`, the `GoodreadsSettings` / `GoodreadsShelfSyncSummary` /
  `GoodreadsProgressSyncSummary` / `GoodreadsSyncResult` records, and the five API members.
  Fix the "appended at the tail, after Goodreads" ordering comments on the Audible members.

**Client (`src/Client/`)**
- Settings: remove the Goodreads card (`goodreadsDetail` and its section entry in `Views.fs`),
  every `Goodreads*` field of the `Model`, every `Goodreads*` `Msg` case and its `update` branch,
  and the `init`-time `getGoodreadsSettings` fetch. Delete `Pages/Settings/GoodreadsCard.test.fs`
  and its `Client.fsproj` line; fix the stray Goodreads reference in `AudibleImportSync.test.fs`.
- `BookDetail/Views.fs` and `Dashboard/Views.fs`: the progress-source label / icon match drops
  its Goodreads arm.
- `tests/e2e/book-detail-progress.spec.ts`: rewrite the header comment that says the spec
  drives a Goodreads-linked book (verify the spec still describes what it tests).

**Tests (`tests/Server.Tests/`)**
- Delete `GoodreadsTests.fs`, `GoodreadsProgressTests.fs`, `GoodreadsSyncTests.fs` and their
  `Server.Tests.fsproj` lines. Every other test file that constructs `Api.create` (about twenty
  — AddGameFromRawg/Steam, AdminSurgery, AudibleApi, AudibleLibrarySync, BookProjection,
  BooksApi, Books, CatalogProjection, DashboardBooks/CardExpansion/Linger,
  GameReleaseDateProjection, NotesRemovalCleanup, OpenLibraryApi/OpenLibrary,
  RequestConnectionConcurrency, SteamFamily*, SteamStorefrontThrottle) loses the two Goodreads
  arguments — the arity change must land in the same commit (see
  `run-full-suite-on-main-after-parallel-batch`: `Api.create` arity is exactly what breaks
  silently).
- Add: a BookProjection test that pre-creates `book_detail` with the legacy `goodreads_book_id`
  column, runs projection init and applies a `Book_added`, proving the orphan column is
  tolerated; a Jobs-tab test that seeds a `job_runs` row with `job_name = 'Goodreads shelf
  sync'` and asserts the scheduled-jobs listing renders without it and without error (the live
  DB may carry such rows from the daily schedule even though the sync never had a user id).

**Knowledge (`.agentheim/knowledge/`) — reported by the worker, materialized by the conductor**
- One new ADR: *Goodreads integration removed* — supersedes ADR-0078 in full (set its
  `superseded_by`), and amends ADR-0075 to retire its Goodreads half while keeping its Open
  Library / Audnexus ruling in force (record the amendment on 0075's `amended_by`/notes the way
  the ADR template does it). State that the ADR-0078 wrapper-JobSpec pattern survives, carried by
  Audible's `runAudibleProgressSyncNow`, and that the removal was a clean delete because no
  persisted event referenced Goodreads (cite the 2026-09-18 dev + live check).
- README deltas: **integration** — drop the "Goodreads" and "Goodreads reading progress"
  ubiquitous-language entries, and the "public feed (Goodreads)" mention; **books** — trim
  "External id", "Format", "Personal rating", "Finished on", "Downstream of" and the
  "External progress sources (Audible, Goodreads)" actor line to Audible/Open Library only, and
  delete the "Goodreads reading progress has no confirmed public source" open-question bullet
  (it is moot); **journal** — the "Reading day" entry cites Goodreads only as an example source;
  reword.
- `knowledge/index.md`'s books line ("sourced from Audible, Goodreads or the user") — conductor
  edit alongside the README deltas.
- `vision.md` and `context-map.md` were already amended at capture time (this task's capture
  commit) — nothing left for the worker there.

## Acceptance criteria

- [ ] `rg -i goodreads src tests` returns no matches.
- [ ] `src/Server/Goodreads.fs`, `src/Server/GoodreadsSync.fs`,
      `src/Client/Pages/Settings/GoodreadsCard.test.fs`, `tests/Server.Tests/GoodreadsTests.fs`,
      `tests/Server.Tests/GoodreadsProgressTests.fs` and `tests/Server.Tests/GoodreadsSyncTests.fs`
      are deleted, and no `.fsproj` references them.
- [ ] `IMediathecaApi` has no Goodreads members; `Api.create` no longer takes a Goodreads config
      provider or a Goodreads sync runner; every test call site compiles.
- [ ] `ProgressSource` is exactly `Audible | Manual`; `ExternalId` has no `GoodreadsBookId` case;
      `BookDetail` has no `GoodreadsBookId` field; the effective-progress precedence in
      `BookProjection` is Manual > Audible with no third rung.
- [ ] Open Library's edition decoder no longer reads `identifiers.goodreads`, and adding a book
      from Open Library attaches only ISBN / Open Library ids (existing OpenLibrary tests updated).
- [ ] Expecto test: projection init against a pre-existing `book_detail` table that still has the
      `goodreads_book_id` column succeeds and a `Book_added` projects into it.
- [ ] Expecto test: with a seeded `job_runs` row whose `job_name` is `Goodreads shelf sync`, the
      Jobs tab's scheduled-jobs listing contains no Goodreads job and does not error.
- [ ] The Settings page renders no Goodreads card and the Jobs tab lists no "Goodreads shelf
      sync" job (Vitest for the Settings model/view; Playwright or a DOM check for the page).
- [ ] The new ADR exists, supersedes ADR-0078 (both frontmatters linked) and amends ADR-0075
      (Goodreads half retired, Open Library / Audnexus half kept), and names the surviving
      wrapper-JobSpec pattern under Audible.
- [ ] README deltas are reported for integration, books (including removal of the Goodreads
      open-question bullet) and journal.
- [ ] `npm run build`, `npm test` and `npm run test:client` are green.

## Notes

- Evidence for the clean delete (2026-09-18): read-only queries against a temp copy of
  `~/app/mediatheca/mediatheca.db` (dev) and of `/mnt/media/mediatheca/mediatheca.db` on harbour
  (live) — `events.data LIKE '%Goodreads%'` → 0 rows in both; `settings.key LIKE 'goodreads%'`
  → 0 rows in both; `book_detail.goodreads_book_id IS NOT NULL` → 0 in both. If the worker
  finds either count non-zero at implementation time (the live check is a snapshot), stop and
  bounce — the DU cases would then need to stay as decode-only legacy arms instead.
- `job_runs` rows for "Goodreads shelf sync" may exist on live (the daily job ran and reported
  "skipped: not configured"); the Jobs tab reads runs per known job name
  (`Administration.getRecentRuns`), so orphan rows are inert — the seeded-row test pins that.
- ADR-0077 (Finished status carries `effectiveOn`) cites Goodreads `user_read_at` as its
  motivating example. The ruling stays — Audible `is_finished` and a manual back-dated finish
  still need it — so 0077 is untouched; the new ADR may mention that its Goodreads example is
  historical.
- The research report `goodreads-reading-progress-and-book-metadata-sources-2026-09-16` stays as
  a historical record; its Open Library findings still ground the kept adapter.
- Worker discipline reminder: never touch the live DB (`workers-never-touch-live-database`);
  the evidence above was gathered by the builder side at capture.
- Styleguide gate: `design-system-001-formalize-styleguide` is done; this task removes UI only.
