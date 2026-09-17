---
id: 0083
title: Goodreads integration removed — Audible and Open Library are the only book sources; the ADR-0078 wrapper-JobSpec pattern survives under Audible
scope: integration
status: accepted
date: 2026-09-18
supersedes: [0078]
superseded_by: []
amends: [0075]
related_tasks: [integration-sfmxg, integration-wmqn3, integration-y2ak4]
related_adrs: [0075, 0076, 0077, 0078]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
---

# ADR 0083: Goodreads integration removed

## Context

The Goodreads shelf/progress sync (integration-wmqn3, integration-y2ak4; ADR-0075's Goodreads
half; ADR-0078's wrapper-JobSpec trigger) shipped on 2026-09-16 and was never configured or used
in practice: the builder decided on 2026-09-18 that the Goodreads path is not wanted, and that
Audible (library, listening progress) and Open Library (search, metadata, covers) are the book
sources Mediatheca keeps.

As of 2026-09-18, read-only queries against a temp copy of the dev event store
(`~/app/mediatheca/mediatheca.db`) and the live store on harbour
(`/mnt/media/mediatheca/mediatheca.db`) both showed: `events.data LIKE '%Goodreads%'` → 0 rows;
`settings.key LIKE 'goodreads%'` → 0 rows; `book_detail.goodreads_book_id IS NOT NULL` → 0 rows.
No persisted event ever carried `ProgressSource.Goodreads` or `ExternalId.GoodreadsBookId`. That
makes this a clean deletion — the discriminated-union cases can be removed outright with no
decode-only legacy arm needed, and ADR-0002/ADR-0043's "nothing in the log is left undecodable"
doctrine is honored trivially (there is nothing in the log to decode).

`job_runs` rows for "Goodreads shelf sync" may still exist on live (the daily job fired and
recorded "skipped: not configured" every night) — these are inert once the job is deregistered,
since `IAdminApi.getJobStatuses` iterates the registered `scheduledJobs` list, not `job_runs`
itself.

## Decision

Delete every Goodreads-specific surface in one change:

- **Server:** `Goodreads.fs` and `GoodreadsSync.fs` deleted outright (and their `Server.fsproj`
  `<Compile Include>` lines). `Composition.fs` loses `getGoodreadsConfig`, `goodreadsSyncHour`, the
  "Goodreads shelf sync" `JobSpec`, and `runGoodreadsShelfSyncNow`. `Api.fs` loses the five
  `IMediathecaApi` members (`getGoodreadsSettings`, `setGoodreadsUserId`,
  `setGoodreadsImportShelves`, `testGoodreadsConnection`, `runGoodreadsShelfSync`),
  `decode/encodeGoodreadsImportShelves`, and the `GoodreadsBookId` attachment at add-from-Open-
  Library time. `OpenLibrary.fs`'s edition decoder no longer reads `identifiers.goodreads`.
  `BookProjection.fs`/`Books.fs` lose the `Goodreads`/`GoodreadsBookId` encode-decode branches, the
  `goodreads_book_id` column from `book_detail`'s CREATE/INSERT/SELECT (and its now-unnecessary
  partial UNIQUE index), and the `WHEN 'Goodreads' THEN 2` precedence rung.
- **Shared:** `ProgressSource` is now exactly `Audible | Manual`; `ExternalId`/`BookExternalId` has
  no `GoodreadsBookId` case; `BookDetail` has no `GoodreadsBookId` field; the `GoodreadsSettings`,
  `GoodreadsShelfSyncSummary`, `GoodreadsProgressSyncSummary` and `GoodreadsSyncResult` records and
  the five API members are gone from `IMediathecaApi`.
- **Client:** the Settings Goodreads card, its `Model` fields, `Msg` cases and reducer branches are
  deleted; `BookDetail`/`Dashboard` views' progress-source label/icon match drops its Goodreads arm;
  `GoodreadsCard.test.fs` is deleted.
- **Precedence:** `BookProjection`'s effective-progress precedence (ADR-0076 §2) is now
  **Manual > Audible** with no third rung. The rule itself (latest-wins by day, ties broken by
  source priority) is unchanged; it simply has one fewer source to break ties against.
- **Existing data:** an existing projection DB that still carries the orphan, nullable
  `book_detail.goodreads_book_id` column, or a `job_runs` row for "Goodreads shelf sync", keeps
  working untouched — the column is never selected or written by the new code (`CREATE TABLE IF
  NOT EXISTS` never touches an existing table's columns), and the orphan job-run row is simply
  never listed once its job is no longer in the registry. No migration is needed or performed.
- **The ADR-0078 wrapper-JobSpec pattern survives**, now solely carried by Audible's
  `runAudibleProgressSyncNow` in `Composition.fs`: a wrapper `JobSpec` sharing its real spec's
  `Name` (so `tryStartJob`'s name-keyed guard is the identical slot), closing over a local
  `resultCell` to hand the Settings card's "Sync progress now" click its typed
  `AudibleProgressSyncResult` synchronously while still recording a `job_runs` row and honouring
  the overlap guard. This ADR **supersedes ADR-0078** in full — the pattern it recorded is not
  retired, only its Goodreads carrier is.
- **This ADR amends ADR-0075**: §§1–4 (the Goodreads shelf/progress sync) are retired; §5 (Open
  Library as book search/metadata source, Audnexus as the audiobook metadata fallback) stays in
  force, unchanged.
- ADR-0077 (status changes carry an `effectiveOn` date) is untouched — Audible's `is_finished` and
  a manual back-dated finish still need it. Its Goodreads `user_read_at` example is now historical.

## Consequences

### Positive
- Removes a Settings card and a scheduled job that would otherwise sit permanently unconfigured.
- Shrinks `Api.create`'s parameter surface (and every test's construction of it) by two arguments.
- `ProgressSource`/`BookExternalId` are simpler DUs with one fewer case each to handle everywhere.

### Negative
- The `read`/`to-read` shelf-import path and the public-status-feed progress path (the actual
  research payoff of `goodreads-reading-progress-and-book-metadata-sources-2026-09-16`) are gone;
  re-adding either later means re-deriving them from that research report rather than reviving code.
- Existing dev/live `book_detail.goodreads_book_id` columns and `job_runs` rows for "Goodreads
  shelf sync" are left in place as harmless orphans rather than cleaned up — acceptable per this
  task's own instructions (no migration needed), but a future schema audit will see them.

### Neutral
- The research report stays as a historical record; its Open Library findings still ground the
  kept adapter.

## Alternatives considered

- **Keep the DU cases as decode-only legacy arms.** Rejected: the 2026-09-18 dev+live check found
  zero persisted events/settings/ids referencing Goodreads in either store, so there is nothing to
  decode — keeping dead cases "just in case" would only re-add the vocabulary this task removes.
- **Run a cleanup migration against `settings`/`book_detail.goodreads_book_id`/`job_runs`.**
  Rejected per this task's own instructions: no row with any Goodreads-prefixed setting key exists
  in dev or live, the orphan column is nullable and inert, and orphan `job_runs` rows are already
  invisible to `getJobStatuses` once the job is deregistered.

## References

- `goodreads-reading-progress-and-book-metadata-sources-2026-09-16` (research report)
- ADR-0075, ADR-0076, ADR-0077, ADR-0078
- Done tasks: integration-wmqn3, integration-y2ak4

## Note on ADR numbering

Minted provisionally as ADR-0082 in its worker worktree. A sibling task's ADR already claimed that number (or the guess overshot the true count) by the time this task's conductor finalized numbering at squash-merge integration (`lib/adr-allocation.mjs`'s `finalizeAdrNumbering`, ADR-0058) — this ADR was renumbered to **ADR-0083**, the true next-free number on `main` at that moment. No content besides this identity changed.
