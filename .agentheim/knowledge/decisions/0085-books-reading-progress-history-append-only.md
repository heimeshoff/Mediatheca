---
id: 0085
title: Reading-progress history is append-only — a prior or observation is never overwritten by later listening data, even same day/same source; a history entry is identified by the recording event's own store position (entry id), the no-op rule compares percent AND position, and removal is keyed by entry id
scope: books
status: accepted
date: 2026-09-18
supersedes: []
superseded_by: []
amends: [0076, 0082]
related_tasks: [books-wk67x, books-d4wtc, integration-fn3yx]
related_research: []
---

# ADR 0085: Reading-progress history is append-only (amends ADR-0076 §2, ADR-0082 §7)

## Context

ADR-0076 §2 gave `Reading_progress_observed` the natural key `(bookSlug, observedOn, source)`:
a second observation on the same day from the same source **replaced** the earlier one in the
projection ("latest wins; the day is what the Journal counts"), and the aggregate tracked
`Observations: Map<(observedOn, source), percent>` — one slot per day/source, not a history.

This collapsed on 2026-09-18 in exactly the way that matters most: "Import library" recorded a
`Prior_reading_progress_recorded` for a book at 14 % / 75 of 539 minutes, dated today (the
import-day fallback). Five minutes later, a Settings-triggered Audible sync appended a
`Reading_progress_observed` for the same day and source. Because the natural key was
`(slug, observedOn, source)` in both the aggregate's map and the `book_progress` primary key
(`ON CONFLICT ... DO UPDATE`), the observation **replaced the prior**: the starting position was
gone, `kind` flipped from `'prior'` to `'observation'`, and the book's whole Audible history became
a single row. The builder's ruling (2026-09-18, verbatim): *"An entry with new listening data should
never overwrite the prior entry in the history. The prior entry is set and will not be overwritten.
Changes in minutes or percent will add as new history entries instead of overwriting it even on the
same day."*

`integration-fn3yx` (a minutes-based Audible sync, dependent on this task) makes the problem
worse, not better, if left unfixed: a sync that writes same-day entries far more often would
overwrite priors constantly.

## Decision

1. **A history entry's identity is the store position of the event that recorded it ("entry
   id"), not `(observedOn, source)`.** Production command execution (`Api.fs`'s
   `executeBookCommandWithEvents`, `AudibleSync.fs`'s own local copy) supplies each loaded
   event's real `StoredEvent.GlobalPosition` when reconstituting `Books.ActiveBook`; the
   projection (`BookProjection.fs`'s `handleEvent`) independently uses the SAME `StoredEvent.
   GlobalPosition` when inserting a `book_progress` row — so the id the client is ever handed
   (via `ReadingProgressDto.EntryId`) is exactly the id the aggregate recognizes for removal. No
   new payload field on `ReadingProgressObservedData` — the event payload is unchanged, so
   existing events need no upcast and no re-serialization.

   `Books.fs` adds a lightweight second reconstitution path, `reconstituteEvents : BookEvent
   list -> BookState`, that assigns each event a synthetic-but-equally-monotonic 1-based
   sequence instead of a real store position. This is a deliberate, narrower choice than the
   task's own suggested default of threading `global_position` through every call site
   uniformly: `Books.decide`/`Books.evolve`'s own logic never depends on the entry id's actual
   *value*, only on it being unique and increasing in append order (used for the per-source
   "latest entry" comparison and for matching a removal command's id against `Observations`) —
   so a cheap synthetic sequence is exactly as correct for every existing aggregate-level test
   (which never round-trips an id through a real store) as the real thing, at a fraction of the
   blast radius: none of the ~10 existing `Api.fs` call sites that go through the shared,
   BC-agnostic `executeCommandCore` needed to change their loading mechanics or their test
   fixtures' shape. Production paths always use the real `GlobalPosition` (via a dedicated
   `executeBookCommandWithEvents`/`executeBookCommand` pair in `Api.fs`, no longer routed
   through `executeCommandCore` for Books at all), so the removal-by-id round trip is exactly as
   correct as the fully-threaded design would have been.

2. **`ActiveBook.Observations` becomes an ordered list of entries, not a map.** `type
   ObservationEntry = { EntryId; ObservedOn; Source; Percent; Position; Kind }`, appended in
   event order, never removed except by an explicit removal event. `latestEntryForSource`
   replaces `latestPercentForSource`: the same source's latest entry, ordered by `(ObservedOn,
   EntryId)` descending, so two same-day same-source entries pick the one recorded LAST.

3. **The no-op rule compares percent AND position.** `decideObserveProgress` no-ops only when
   the incoming `(Percent, Position)` both equal the source's latest entry's — the virtual
   baseline `{ Percent = 0; Position = None }` when the source has no entry yet, preserving the
   pre-existing "a first observation at 0 % is a no-op" behaviour exactly, while now also
   recording a first-ever 0 %-with-a-known-position observation (a strict improvement: the old
   rule silently dropped this too, an instance of the same "lose the very first data point"
   defect this ADR exists to fix). `Record_prior_reading_progress`'s existing-source check moves
   from `Map.exists` to `List.exists`; its own first-ever-prior branch is otherwise unchanged
   (ADR-0082 §2's "including at 0 %" still holds).

4. **A new event/command pair, keyed by entry id, replaces the old day+source removal.**
   `Reading_progress_entry_removed of entryId: int64` / `Remove_reading_progress_entry of
   entryId: int64` are the ONLY removal command/event a fresh `decide` call ever issues.
   `Reading_progress_observation_removed of observedOn * source` (the old shape) is demoted to
   **replay-only**: it stays in `BookEvent` and keeps its ORIGINAL meaning on `evolve` — removes
   every entry of that day/source that exists at that point in the stream (there was at most one
   before this task, since same-day/source always collapsed) — but `BookCommand` drops
   `Remove_reading_progress_observation` entirely; nothing issues it any more. Per ADR-0082 §7
   ("removing a book's only prior for a source empties that source's history"), a user removing a
   prior by hand stays legal — "append-only" means new listening data never overwrites a prior,
   not that the user can't delete one. `BookProjection`'s legacy handler code is byte-identical
   to before (`DELETE ... WHERE book_slug = ? AND observed_on = ? AND source = ?`) — it already
   naturally deletes however many rows now match, which is exactly the desired legacy semantics.
   The book detail page's per-row remove (`removeBookProgressEntry`, replacing
   `removeBookProgressObservation` in `IMediathecaApi`) and `Api.importAudibleLibraryImpl`'s
   legacy repair both moved to the new command; `BookProjection.legacyObservationToRepair` now
   returns `(entryId, observedOn)` instead of just `observedOn`.

5. **`book_progress` drops its `(book_slug, observed_on, source)` primary key for `entry_id`
   alone** (globally unique, since it's the event's real `global_position` for every row written
   from this point forward) — a genuine schema change SQLite can't do via `ALTER TABLE ADD
   COLUMN`. A pre-existing table is migrated in place at `Init` time: renamed, recreated with the
   new shape, and its rows copied across with `entry_id` recovered via a correlated subquery
   against the book's own event stream (`json_extract` matching `(observedOn, source)` to the
   LATEST qualifying event, mirroring `GameProjection.fs`'s own `retired_at` backfill idiom),
   falling back to `-rowid` (negative — real `global_position` values are always positive, so
   this can never collide) on the rare row with no matching event. This backfill is honestly
   approximate for the (already rare) case of a book with more than one historical duplicate
   same-day-same-source event that the OLD upsert had already collapsed to one physical row
   before this task shipped — the earlier duplicate's own data is genuinely unrecoverable from
   `book_progress` alone, and only a true rebuild (an operator's own action, never automatic at
   boot) recovers it, the same drift-acceptance stance this projection's own `progress_kind`
   backfill (books-d4wtc) already established. Both handlers for `Reading_progress_observed`/
   `Prior_reading_progress_recorded` become plain `INSERT`s (no more `ON CONFLICT ... DO UPDATE`).

   **View-safety corollary.** Any `ALTER TABLE ... RENAME` in this codebase revalidates every
   view in the WHOLE schema (a documented SQLite quirk `MetadataCache.fs`'s own
   `recoverStranded` already works around) — this migration's rename can fail with `no such
   table: main.series_episode_cache` if `series_next_up`/`series_episode_counts` exist on the
   connection with no `series_episode_cache` table behind them yet. Both views are dropped
   defensively before the rename and restored immediately after via `MetadataCache.initialize`
   (idempotent, safe to call a second time per boot) — the same drop-then-let-the-owning-
   initializer-recreate idiom `recoverStranded` established, invoked from the migration that
   needs it instead of from inside `MetadataCache.fs` itself.

6. **`recomputeProgress` and the `PagesReadThisYear` stat query re-key on `(observed_on,
   entry_id)`, not `(observed_on, source)`.** The old Manual-over-Audible same-day tie-break is
   retired in favour of "whichever entry was recorded LAST wins" — a simpler rule the append-only
   model makes correct by construction, and the one the task's own text specifies
   ("`recomputeProgress` picks the latest row by `observed_on`, then entry id"). `getBySlug`'s
   `getProgressHistory` now also selects `entry_id` (into `ReadingProgressDto.EntryId`) and
   orders `(observed_on, entry_id)` ascending; the client's History list sorts descending by the
   same pair for "newest first, several per day when they exist" — extracted into its own pure,
   Feliz-free seam (`BookDetail/History.fs`'s `orderNewestFirst`, in the `Progress.fs`/`Format.fs`
   house pattern) rather than inlined in `Views.fs`, so the sort itself is unit-tested, not just
   exercised indirectly through a rendered page. `PagesReadThisYear`'s old JOIN on `(observed_on,
   source)` would now fan out across every same-day entry a finished book carries,
   double-counting pages — replaced with a correlated subquery naming book_progress's own single
   latest row per book, the exact same ordering `recomputeProgress` uses. `HoursListenedThisYear`
   needed no change: it already reads only `book_list`'s single denormalized `progress_percent`,
   which can never fan out.

## Alternatives considered

- **Thread `global_position` through every reconstitution path uniformly, including every
  existing test fixture** — rejected as unnecessarily invasive: it would have required rewriting
  every one of BooksTests.fs's ~30 `given`/`when`/`then` fixtures to carry explicit positions for
  events that never need one, for zero behavioural gain over the synthetic-sequence
  `reconstituteEvents` this ADR adopts instead (see Decision §1).
- **Keep `Remove_reading_progress_observation` as a live command, translating a client's entry
  id back to a `(observedOn, source)` pair at the API edge** — rejected: this would remove the
  WRONG entry whenever two same-day-same-source rows exist (the very case this task exists to
  make possible), defeating the point of naming one entry precisely.
- **A per-book local monotonic counter (unrelated to `global_position`) for entry identity** —
  considered and rejected: the projection and the aggregate would need to agree on the SAME
  numbering space for the removal-by-id round trip to work at all, and a local counter that's
  robust to a removed entry never being renumbered/reused requires bookkeeping the real
  `global_position` conveniently already provides for free at both call sites.
- **Leave the History list's newest-first sort inline in `Views.fs`** — this is how iteration 1
  of this task shipped it; rejected on iteration-2 verifier feedback: the sort is brand-new
  `(string * int64)`-tuple logic under Fable, is the visible payoff of this ADR, and sat where no
  test could reach it. Extracted into `BookDetail/History.fs`'s `orderNewestFirst`, the same
  `Progress.fs`/`Format.fs`/`SeriesDetail/NextUp.fs` house pattern this BC already uses for
  exactly this kind of pure, page-local logic.

## Consequences

- The exact incident this task exists to fix cannot recur: a same-day, same-source observation
  is a new history entry, never a replacement, and a prior stays exactly as recorded until a user
  removes it by hand.
- `book_progress` needed a genuine schema migration (rename + recreate + copy), the first this
  projection has needed beyond additive `ALTER TABLE ADD COLUMN` — mitigated by mirroring
  `CatalogProjection.fs`'s already-proven self-healing idiom, plus the view-safety corollary
  `MetadataCache.fs`'s own precedent already worked out.
- `integration-fn3yx` (a minutes-based Audible sync) can now write same-day entries as often as
  it observes a minutes change without ever overwriting a prior or a same-day sibling.
- One more DU case/projection column pair to keep synchronized going forward
  (`Reading_progress_entry_removed` mirroring the removal shape; `book_progress.entry_id`) —
  mitigated by reusing the existing `ReadingProgressObservedData` payload verbatim for every
  entry-producing event, unchanged by this ADR.
- One more small pure module in `BookDetail/` (`History.fs`) — a net simplification, since the
  sort it carries would otherwise be untestable, private logic inside `Views.fs`.

## References

- `.agentheim/knowledge/decisions/0076-books-progress-observation-events-and-status-lifecycle.md`,
  `0082-prior-reading-progress-and-manual-finish-local-date.md` — amended by this ADR.
- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md` —
  the engagement test this ADR's history entries still pass.
- `src/Server/Books.fs`, `src/Server/BookProjection.fs`, `src/Server/Api.fs`,
  `src/Server/AudibleSync.fs`, `src/Server/EventFormatting.fs`, `src/Shared/Shared.fs`,
  `src/Client/Pages/BookDetail/{Types,State,Views,History}.fs` — the code sites this ADR reasons
  about.
- `src/Server/MetadataCache.fs` (`recoverStranded`'s view-safety fix, `initialize`'s idempotent
  view recreation), `src/Server/CatalogProjection.fs` (`catalogEntriesNeedsWidening`'s
  rename+recreate+copy self-heal), `src/Client/Pages/BookDetail/Progress.fs` +
  `src/Client/Pages/SeriesDetail/NextUp.fs` (the pure-seam house pattern `History.fs` follows) —
  the precedents this ADR's migration and client extraction mirror.
