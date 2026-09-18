---
id: books-wk67x
title: Reading history appends, never overwrites — a prior is set once and no later listening data replaces it, and any change in position or percent adds a NEW history entry even on the same day from the same source (reverses ADR-0076 §2's same-day collapse).
status: todo
type: feature
context: books
created: 2026-09-18
completed:
depends_on: []
blocks: [integration-fn3yx]
tags: [books, reading-progress, prior, history, projection, aggregate]
related_adrs: [0076, 0082, 0077, 0043]
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
