---
id: books-wk67x
title: Reading history appends, never overwrites — a prior is set once and no later listening data replaces it, and any change in position or percent adds a NEW history entry even on the same day from the same source (reverses ADR-0076 §2's same-day collapse).
status: done
type: feature
context: books
created: 2026-09-18
completed:
depends_on: []
blocks: [integration-fn3yx]
tags: [books, reading-progress, prior, history, projection, aggregate]
related_adrs: [0076, 0082, 0077, 0043, 0085]
related_research: []
prior_art: [books-d4wtc, books-y9kxy]
---

## Why
Builder ruling 2026-09-18, verbatim: *"An entry with new listening data should never overwrite the
prior entry in the history. The prior entry is set and will not be overwritten. Changes in minutes
or percent will add as new history entries instead of overwriting it even on the same day."*

Observed on 2026-09-18 (dev): "Import library" recorded a `Prior_reading_progress_recorded` for
`for-we-are-many-2017` at 14 % / 75 of 539 min, dated 2026-09-18 (import-day fallback). Five
minutes later a Settings-triggered Audible sync appended a `Reading_progress_observed` dated the
same day from the same source. Because the natural key is `(slug, observedOn, source)` — in the
aggregate's `Observations: Map<(observedOn, source), percent>` and in `book_progress`'s primary key
with its `ON CONFLICT ... DO UPDATE` upsert — the observation **replaced the prior**: the History
list lost the starting position, `kind` flipped from `'prior'` to `'observation'`, and the book's
whole Audible history became a single row. ADR-0076 §2 designed that collapse on purpose ("latest
wins; the day is what the Journal counts"); this task reverses it.

## What
History entries become append-only facts. Same day and same source no longer identify an entry.

1. **Entry identity.** A history entry (prior or observation) is identified by the store position
   of the event that recorded it, not by `(observedOn, source)`. Use the event's
   `global_position` (already unique, already monotonic) as the entry id carried into the
   projection and the DTO. No new payload field on `ReadingProgressObservedData` — the event
   payload stays as is, so no upcast and no serialization change for existing events.
2. **Aggregate.** `ActiveBook.Observations` stops being a map keyed `(observedOn, source)` and
   becomes an ordered collection of entries that keeps every entry: `{ ObservedOn; Source; Percent;
   Position; Kind }` in append order. `latestPercentForSource` becomes "the source's latest entry",
   ordered by `ObservedOn` and then by append order within a day.
3. **No-op rule.** `Observe_reading_progress` appends nothing only when BOTH percent and position
   equal the source's latest entry. A change in either one — for an audiobook, a change in minutes
   even when the whole percent is unchanged — is a new entry. Promotion, regression and finish
   rules (ADR-0076 §5, ADR-0082 §3) are otherwise unchanged and still compare percent.
4. **A prior is immutable.** Nothing issued by `Observe_reading_progress` or
   `Record_prior_reading_progress` ever replaces, re-dates or re-kinds an existing prior row. At
   most one prior per source per book still holds (ADR-0082 §2: once the source has an entry,
   `Record_prior_reading_progress` behaves as `Observe_reading_progress`).
5. **Projection.** `book_progress` drops the `(book_slug, observed_on, source)` primary key and the
   `ON CONFLICT ... DO UPDATE` upsert in favour of one row per event, keyed by the entry id from
   point 1. Both the `Reading_progress_observed` and `Prior_reading_progress_recorded` handlers
   become plain inserts. `recomputeProgress` picks the latest row by `observed_on`, then entry id.
   This is a projection schema change: bump it the way this projection's existing drop-and-rebuild
   path already does, so a rebuild replays every historical event into its own row.
6. **Removal targets one entry.** `Reading_progress_observation_removed (observedOn, source)`
   cannot name a single entry any more. Add a removal that names the entry id; keep decoding the
   old `(observedOn, source)` event on replay with its old meaning (removes every entry of that
   day and source that existed at that point in the stream) so history replays identically — no
   upcast, per ADR-0082 §6's doctrine. The book page's per-row remove and
   `Api.importAudibleLibraryImpl`'s legacy repair move to the new command. A user removing a prior
   by hand stays legal (ADR-0082 §7) — "immutable" means new listening data never overwrites it,
   not that the user can't delete it.
7. **Client.** `ReadingProgressDto` carries the entry id; the History list renders every entry,
   several per day when they exist, newest first (by `ObservedOn`, then entry id); React keys and
   the remove action use the entry id.
8. **Journal and stats stay day-based.** Anything that counts reading *days* (Journal heatmap,
   activity feeds) must count a day once however many entries it holds. `getReadingStats` keeps
   excluding `kind = 'prior'` (ADR-0082 §5) and must keep keying on the latest entry, not summing
   same-day rows.
9. **ADR.** Write an ADR amending ADR-0076 §2 (same-day collapse and natural key reversed; no-op is
   percent AND position) and ADR-0082 §7 (removal keyed by entry id). Update the Books README's
   "Progress observation", "Prior reading progress" and aggregate-invariant entries to match.

## Acceptance criteria
- [ ] Aggregate test: a prior at 14 % / 75 min dated D, then `Observe_reading_progress` at 11 % / 60 min dated the same D from the same source, yields two entries; the prior is unchanged and still `Prior`.
- [ ] Aggregate test: two observations on the same day from the same source with different minutes but the same whole percent both produce `Reading_progress_observed`.
- [ ] Aggregate test: an observation whose percent AND position equal the source's latest entry produces no events; a daily sync of an untouched library still appends zero events.
- [ ] Aggregate test: `Record_prior_reading_progress` for a source that already has an entry never emits a second prior and never alters the first.
- [ ] Projection test: replaying prior + same-day observation leaves two `book_progress` rows, the first with `kind = 'prior'` and its original percent, position and date; `book_detail.progress_percent` reflects the later entry.
- [ ] Projection test: a full rebuild over a stream containing a legacy `Reading_progress_observation_removed (observedOn, source)` event produces the same surviving rows that event produced before this change.
- [ ] Removal test: removing one entry by id on a day holding two same-source entries leaves the other row, and the aggregate's per-source baseline falls back to the remaining latest entry.
- [ ] `ReadingProgressDto` exposes the entry id; a client test covers the History list rendering two same-day same-source rows and dispatching removal with the right id.
- [ ] A test pins that a day with several entries counts once wherever reading days are counted, and that `HoursListenedThisYear` is not inflated by same-day rows.
- [ ] ADR written amending ADR-0076 §2 and ADR-0082 §7; Books README updated.
- [ ] `npm run build`, `npm test` and `npm run test:client` pass.

## Notes
- Code sites: `src/Server/Books.fs` (`ActiveBook.Observations`, `latestPercentForSource`, `decideObserveProgress`, the `Record_prior_reading_progress` branch, removal), `src/Server/BookProjection.fs` (table DDL near line 64, both inserts near lines 392 and 416, removal near 435, `recomputeProgress` near 242, history read near 515, `hasProgressFromSource`-style helpers near 590–612), `src/Server/Api.fs` (legacy repair near 2138, removal endpoint near 4373), `src/Shared/Shared.fs` (`ReadingProgressDto`), `src/Client/Pages/BookDetail/Views.fs` (`progressHistoryRow`, line 286) and its `State.fs`.
- Capture defaults chosen without the builder in the loop, open to a veto before `work` runs: entry id = event `global_position` (point 1); legacy removal events keep their old replay meaning instead of an upcast (point 6); the no-op compares percent AND position for every source, Manual included (point 3).
- The dev database is disposable — no data repair for the overwritten 2026-09-18 prior is wanted.
- `integration-fn3yx` (Audible sync decides on minutes) depends on this task: a minutes-based sync writes same-day entries far more often, which under today's collapse would overwrite priors constantly.

## Verifier note (iteration 1)

**VERDICT: FAIL** — the runner is green (build OK, Expecto 977/977, Vitest 122/122), README_DELTA checked clean (all five sections exist, both `replace` ops' `expected` text matches), the ADR body is well-formed, no `.agentheim/` path in the diff. One acceptance criterion is only half covered.

**REASONS:**
- Acceptance criterion "`ReadingProgressDto` exposes the entry id; a client test covers the History list **rendering two same-day same-source rows** and dispatching removal with the right id" is only half covered. `src/Client/Pages/BookDetail/State.test.fs:83-115` covers the dispatch half with a two-same-day-same-source fixture, but nothing covers the History list itself: no test asserts that two entries sharing `(ObservedOn, Source)` both reach the list, and nothing covers the new newest-first ordering `historySection` introduces (`src/Client/Pages/BookDetail/Views.fs:368`, `List.sortByDescending (fun r -> r.ObservedOn, r.EntryId)`). That sort is brand-new logic, is the visible payoff of the task (`## What` point 7), is private inside `Views.fs` where no test can reach it, and its `(string * int64)` tuple comparison under Fable is exercised nowhere. The Outcome carries no manual-exercise note, so the no-render-infra carve-out does not apply. The house pattern already exists (`SeriesDetail/NextUp.fs` + `NextUp.test.fs`; `BookDetail/Progress.fs` + `Progress.test.fs`; `BookDetail/Format.fs` + `Format.test.fs`) — the gap is a skipped seam extraction, not missing infrastructure.
- Minor, `src/Server/BookProjection.fs:850-854`: `getReadingStats`'s doc comment still describes `PagesReadThisYear` as joining "the `(book_slug, observed_on, source)` primary key ..." — that join was replaced in this same diff by the correlated latest-row subquery (lines 876-887), and the primary key it names no longer exists.

**SUGGESTED_FIX:** Extract the History list's ordering into a pure, DTO-only seam (e.g. `BookDetail/History.fs`'s `orderNewestFirst : ReadingProgressDto list -> ReadingProgressDto list`, called by `Views.historySection`) and add a client test that feeds it two same-day same-source rows plus an older-day row, asserting both same-day entries survive and come back newest-first by `(ObservedOn, EntryId)`; then refresh the stale `getReadingStats` doc comment.

**ITERATION_HINT:** likely-fixable

## Outcome

Reading-progress history is append-only now (ADR-0085, amending ADR-0076 §2 and ADR-0082 §7): a
prior or an observation is never overwritten by later listening data, even same day/same source —
the exact incident (a same-day Audible sync clobbering an import-recorded prior) this task was
captured to fix. Landed over two iterations: iteration 1 built the full aggregate/projection/API/
client change; iteration 2 (this one) responded to verifier feedback by extracting the History
list's newest-first ordering into its own tested seam and refreshing one stale doc comment — no
other changes.

**Aggregate (`src/Server/Books.fs`).** `ActiveBook.Observations` is an ordered `ObservationEntry
list` now (`{ EntryId; ObservedOn; Source; Percent; Position; Kind }`), not a `Map<(observedOn,
source), percent>`. `evolve`/`reconstitute` are position-aware (`(int64 * BookEvent) list ->
BookState`) so every history-producing event carries its real store position into the entry it
creates; `reconstituteEvents` is a convenience wrapper (bare `BookEvent list`, synthetic 1-based
ids) for callers — every existing test, and any future one — that only need correct append
ORDER, never a real removal-by-id round trip. `latestEntryForSource` replaces
`latestPercentForSource`; `decideObserveProgress`'s no-op rule now compares percent AND position
against the source's latest entry (a virtual `{ Percent = 0; Position = None }` baseline when the
source has none yet, preserving the old "first 0% is a no-op" behaviour exactly while, as a
side-effect, no longer silently dropping a first-ever 0%-with-a-known-position observation).
`Record_prior_reading_progress`'s existing-source check moved to `List.exists`. New
`Reading_progress_entry_removed of entryId: int64` event / `Remove_reading_progress_entry of
entryId: int64` command replace `Remove_reading_progress_observation` as the ONLY removal path a
fresh `decide` call issues; the old day+source event (`Reading_progress_observation_removed`)
survives in `BookEvent` for replay only, with its exact original meaning preserved on `evolve`
(removes every entry of that day/source existing at that point in the stream) — no upcast.

**Projection (`src/Server/BookProjection.fs`).** `book_progress` drops its
`(book_slug, observed_on, source)` primary key for `entry_id` alone (the recording event's real
`global_position`) — a genuine schema change, migrated in place for a pre-existing database via
rename + recreate + copy (`bookProgressNeedsEntryIdMigration`, detected from `sqlite_master`'s
stored DDL text, the same idiom `CatalogProjection.fs`'s own widened-UNIQUE self-heal uses), each
row's `entry_id` recovered via a correlated subquery against the book's own event stream (falling
back to a collision-proof negative synthetic id on the rare row with no matching event). The
migration's `ALTER TABLE ... RENAME` collides with a documented SQLite quirk
(`MetadataCache.fs`'s own `recoverStranded` doc comment) where any rename revalidates every view
in the schema — `series_next_up`/`series_episode_counts` are dropped defensively before the
rename and restored via `MetadataCache.initialize` immediately after. `Reading_progress_observed`/
`Prior_reading_progress_recorded` handlers are plain `INSERT`s now (no more
`ON CONFLICT DO UPDATE`); the legacy day+source removal handler is untouched (its existing
`DELETE ... WHERE book_slug = ? AND observed_on = ? AND source = ?` already deletes however many
rows now match). `recomputeProgress` and `getReadingStats`'s `PagesReadThisYear` re-key on
`(observed_on, entry_id)` — the old Manual-over-Audible tie-break is retired for "whichever entry
was recorded last wins"; `PagesReadThisYear`'s old JOIN (which would now fan out and double-count
across same-day duplicates) becomes a correlated subquery naming the one latest row per book.
`getProgressHistory` selects `entry_id` into `ReadingProgressDto.EntryId` and orders
`(observed_on, entry_id)`. `legacyObservationToRepair` returns `(entryId, observedOn)`.
**Iteration 2:** the `getReadingStats` doc comment (verifier reason 2) was refreshed — it still
described `PagesReadThisYear` as joining on the now-dropped `(book_slug, observed_on, source)`
primary key; it now correctly describes the correlated-subquery approach the same diff already
shipped.

**API (`src/Server/Api.fs`, `src/Server/AudibleSync.fs`).** Every Books command now goes through a
dedicated, position-aware `executeBookCommandWithEvents`/`executeBookCommand` pair (moved earlier
in `Api.fs`, ahead of `addBookToLibraryImpl`) rather than the shared, BC-agnostic
`executeCommandCore` — the ~10 call sites that used to build `Books.reconstitute events` directly
now call `executeBookCommand conn slug command projectionHandlers`. `AudibleSync.fs`'s own local
copy of the same helper is updated identically. `removeBookProgressEntry` (new
`IMediathecaApi` member, replacing `removeBookProgressObservation`) and the legacy-repair call
site both issue `Remove_reading_progress_entry`.

**Client (`src/Client/Pages/BookDetail/{Types,State,Views,History}.fs`).**
`ConfirmingRemoveObservation` is `int64 option` now; `Confirm_remove_observation`/
`Remove_observation` carry an entry id, not a `(observedOn, source)` pair; `progressHistoryRow`
keys React rows and dispatches by `row.EntryId`. **Iteration 2 (verifier reason 1):** the
newest-first sort `historySection` used to run inline (`List.sortByDescending (fun r ->
r.ObservedOn, r.EntryId)`, private, unreachable by any test) is now `History.fs`'s
`orderNewestFirst : ReadingProgressDto list -> ReadingProgressDto list` — a pure, Feliz-free
module in the exact `Progress.fs`/`Format.fs` house pattern this BC's own page already
established (mirroring `SeriesDetail/NextUp.fs`'s precedent too), registered in `Client.fsproj`
right after `Format.fs` (source) and after `Format.test.fs` (tests) to keep the existing compile
order. `Views.historySection` now calls `orderNewestFirst book.ProgressHistory` instead of
carrying the sort itself.

**Shared (`src/Shared/Shared.fs`).** `ReadingProgressDto` gains `EntryId: int64`;
`IMediathecaApi.removeBookProgressEntry: string -> int64 -> Async<Result<unit, string>>` replaces
`removeBookProgressObservation`.

**Event browser (`src/Server/EventFormatting.fs`).** New `tryFieldInt64` helper and a
`"Reading_progress_entry_removed"` display case.

**Tests.** `tests/Server.Tests/BooksTests.fs`: 5 new aggregate cases (prior + same-day
same-source observation leaves two entries with the prior untouched; two same-day observations
with different minutes but the same whole percent both record; an observation matching percent
AND position is the only genuine no-op; `Record_prior_reading_progress` on a source with an
existing prior never emits a second one; removing one entry by id among two same-source
same-day entries leaves the other and the per-source baseline falls back to it), plus the two
pre-existing removal-command tests converted to the new by-id shape and the round-trip/
`handledEventTypes` tests extended for `Reading_progress_entry_removed`.
`tests/Server.Tests/BookProjectionTests.fs`: 4 new projection cases (prior + same-day observation
leaves two rows with the prior's own percent/position/date intact and `book_detail` following the
later entry; a full `Projection.rebuildProjection` over a legacy day+source removal leaves the same
zero surviving rows it produced before this task; `HoursListenedThisYear` / `PagesReadThisYear`
are each not inflated by several same-day entries for one finished book), plus the two collapse-
behavior tests rewritten to assert the new append-only outcome instead (one of them, the
cross-source tie-break, keeps the same numeric expectation for a new reason — recorded-last, not
source priority — and says so). `tests/Server.Tests/AudibleLibrarySyncTests.fs`: the two
`ReadingProgressDto` literal-equality assertions that predate `EntryId` are rewritten to compare
every OTHER field (the id is a real, unpredictable `global_position` in an integration test); the
"already has an Audible row" test's expectation is corrected from a same-day collapse to the
prior surviving alongside a new second entry. `src/Client/Pages/BookDetail/State.test.fs`: 2
cases (from iteration 1) proving `Confirm_remove_observation`/`Remove_observation` track and
dispatch the SPECIFIC row's entry id, never confusing it with a sibling row sharing the same day
and source. **New in iteration 2:** `src/Client/Pages/BookDetail/History.test.fs` — 3 cases for
`orderNewestFirst`: two same-day same-source rows plus an older-day row all survive and come back
newest-first by `(ObservedOn, EntryId)`; an empty history orders to an empty list; a single entry
orders to itself.

`npm run build`, `npm test` (Expecto, 977 passing) and `npm run test:client` (Vitest, 125 passing,
up from 122 — the 3 new `History.test.fs` cases) are all green.

ADR-0085 (provisional numbering — main has since gained a sibling's ADR-0084) and Books README
updates travel in this RESULT's `ADRS`/`README_DELTA` blocks, per this task's own instructions —
not written to disk by this worker. Both blocks are repeated here in full per the conductor's
iteration-2 instructions; nothing from iteration 1 landed on `main`.
