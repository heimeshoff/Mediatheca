---
id: 0082
title: A source's first-ever reading-progress report per book is a prior, not a session — `Prior_reading_progress_recorded` (never `InFocus`-promoting, dated by the source's own last-known-true day when available), and a manual Finished click stamps today's local date instead of the event's UTC append timestamp
scope: books
status: accepted
date: 2026-09-18
supersedes: []
superseded_by: []
amends: [0076, 0077]
related_tasks: [books-d4wtc, integration-dtdbb, books-xyqyb]
related_research: [audible-finished-and-last-listened-timestamps-2026-09-18]
---

# ADR 0082: Prior reading progress and manual-finish local dating (amends ADR-0076 §2, ADR-0077 §5)

## Context

Import and the nightly sync both funnel through `AudibleSync.observationFor`, the one pure decision
that turns an Audible library item into `Observe_reading_progress`, and both emit the same event,
`Reading_progress_observed`, stamped `ObservedOn` = the day the sync/import ran. A first-time import
of an established library therefore writes one observation per title dated *today*, at its full
historical position (e.g. "100 % · 600 of 600 min") — indistinguishable in the History list from a
600-minute same-day listening session — and every already-finished title gets `finished_at = today`
(ADR-0077 §5), even for a book the user actually finished years ago. Research
(`audible-finished-and-last-listened-timestamps-2026-09-18`) found a real, if unverified-in-production,
source for the true date: `GET /1.0/content/{asin}/metadata?response_groups=last_position_heard`
returns `last_position_heard.last_updated`, the day Audible itself last saved the user's position.

Separately, a manual "mark Finished" click (`BookDetail/State.fs`) sends `effectiveOn = None`, and
`BookProjection.fs` defaults that to the event's own **UTC** append timestamp
(`EventStore.fs`'s `DateTimeOffset.UtcNow`) — so a finish recorded after roughly 22:00 local time
(CEST) is dated *yesterday*, and there is no way to correct a wrong `finished_at` from the UI at all.

## Decision

1. **New event `Prior_reading_progress_recorded`**, same payload as `Reading_progress_observed`
   (`ReadingProgressObservedData`, reused verbatim — no new shape). UL term **Prior reading
   progress**: the position a source reports the first time Mediatheca ever sees that source report
   on this book — where the reader already was, not what they read that day. Named to match the
   codebase's own existing precedent for exactly this situation in a sibling BC,
   `Prior_play_time_recorded` (Games, ADR-0043) — not "baseline", which this BC's `Observations`
   comparison (`Books.latestPercentForSource`) and the README's "Progress regression" entry already
   use for a different concept (the per-source no-op comparison point).
2. **Discriminator**: `Books.decide`'s `Observe_reading_progress` branch tests, before the existing
   per-source no-op comparison, whether `data.Source <> Manual` and the source has **no** entry at
   all yet in `Observations` (`Map.exists` over the source, not `latestPercentForSource`'s 0
   default). When true, it emits `Prior_reading_progress_recorded` — **including at 0 %** (a
   first-ever 0 % report must not be silently dropped by the same-percent no-op the way a genuine
   second 0 %-again observation would be). `Source = Manual` always stays a real observation — the
   user is present and the day is real.
3. **A prior never promotes.** It seeds `Observations` exactly like an observation (so later
   no-op/regression comparisons work identically), and at 100 % or an explicit `Finished` flag it
   still emits `Book_status_changed (Finished, Some data.ObservedOn)` per ADR-0077 §5 — but it never
   promotes `Backlog`/`Abandoned` → `InFocus`. The aggregate stays free of recency heuristics; if an
   adapter wants a half-read import to read as "currently reading" that is an explicit, edge-owned
   `Change_status` call, never an aggregate inference.
4. **`ObservedOn` keeps its existing meaning.** It has always meant "the day the observation is
   about", not "the day Mediatheca learned it" (that is the event's own store timestamp) — ADR-0077
   §5 and the Goodreads `user_read_at` backdate already establish this. A prior's `ObservedOn` is
   therefore honestly historical when Audible's `last_position_heard.last_updated` is known (date
   part only, no timezone conversion), and falls back to the import/sync day when it is not (Audible
   reports `DoesNotExist`, or the call fails) — never a new field, never a split of "when true" from
   "when learned".
5. **Projection carries `kind`.** `book_progress` gains `kind` (`'prior' | 'observation'`);
   `book_list`/`book_detail` gain a denormalized `progress_kind` alongside `progress_percent`/
   `progress_source`/`progress_observed_on` so query-time filtering doesn't require a join.
   `getReadingStats`'s `HoursListenedThisYear` — which multiplies `progress_percent × runtime` for
   any Audible book Finished this year — must exclude `progress_kind = 'prior'`: without this, an
   import-day-fallback-dated prior would book an entire audiobook's runtime as "listened this year",
   the exact same defect this ADR exists to prevent, just landing in a dashboard stat instead of the
   History list. `PagesReadThisYear` already self-excludes (it filters on `finished_at`'s year, and a
   correctly-backdated prior's `finished_at` won't fall in the current year).
6. **No upcast.** Historical `Reading_progress_observed` events are never reclassified on replay — a
   rebuild has no way to honestly guess which past observations were "really" first-imports, so it
   doesn't try.
7. **Removal is uniform.** `Reading_progress_observation_removed` covers both kinds (keyed on
   `(observedOn, source)`, not on kind); removing a book's only prior for a source empties that
   source's history, so the next `Observe_reading_progress` is correctly re-classified as a fresh
   prior. This is the mechanism `integration-dtdbb`'s legacy repair relies on to re-date entries
   written before this ADR shipped.
8. **Manual Finished stamps local today.** `Set_book_status Finished` sends
   `Some (DateTime.Now.ToString("yyyy-MM-dd"))`, matching how `AudibleSync`/`importAudibleLibraryImpl`
   already stamp `ObservedOn` in local time — never `None` (which resolves to the event's UTC append
   date). The hero's `finished {date}` line becomes click-to-edit (`EditableDateInput`, the same
   pattern `MovieDetail`'s watch-session dates already use); re-dating an already-Finished book is a
   legitimate event under ADR-0077 §4, not a no-op, and a future date is rejected at the edge.

## Consequences

- A first-time Audible import (or a newly-bought title's first nightly sync) reads honestly in the
  History list as a starting position, never a listening session, and a finished title's `finished_at`
  reflects when Audible last saw the user reading it, not when Mediatheca happened to import it.
- `integration-dtdbb` costs one extra authenticated call per book that has never had an Audible
  `book_progress` row — bounded to new/never-synced titles, never the whole library on every sync.
- The manual-finish UTC-midnight drift (a book finished tonight showing as finished yesterday) is
  fixed, and a wrong or approximate `finished_at` — including one recovered by legacy repair — is now
  correctable from the book detail page itself.
- Two more DU cases/projection columns to keep synchronized (`Prior_reading_progress_recorded`
  mirroring `Reading_progress_observed`'s serialization exactly; `progress_kind` mirroring
  `progress_source`) — mitigated by reusing the existing payload/encoder/decoder verbatim.

## Alternatives considered

- **A boolean flag on `ReadingProgressObservedData` instead of a new event** — rejected: the builder
  explicitly asked for "a different event", and the codebase's own convention (a distinct DU case per
  meaningfully different fact) makes a prior legible in the event explorer (ADR-0020) by type alone,
  not by inspecting a payload field.
- **Naming it `Reading_progress_baseline_recorded`** (the skill's original draft) — rejected: collides
  with "baseline" as already used for `latestPercentForSource`'s per-source comparison value and the
  README's "Progress regression" entry; `Prior_reading_progress_recorded` matches `Prior_play_time_recorded`'s
  existing precedent for the identical situation one BC over.
- **Promoting a prior to `InFocus` when its `ObservedOn` is recent** — rejected: would require the
  aggregate to reason about "recent" relative to wall-clock time, a heuristic ADR-0043's doctrine and
  this BC's existing design both avoid; an adapter can issue an explicit `Change_status` if desired.

## References

- `.agentheim/knowledge/decisions/0076-books-progress-observation-events-and-status-lifecycle.md`,
  `0077-book-status-change-carries-effective-on-date.md` — amended by this ADR.
- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md` —
  `Prior_play_time_recorded` precedent this ADR's naming follows.
- `.agentheim/knowledge/research/audible-finished-and-last-listened-timestamps-2026-09-18.md` —
  `last_position_heard.last_updated` finding this ADR's dating rule is built on.
- `src/Server/Books.fs`, `src/Server/AudibleSync.fs`, `src/Server/BookProjection.fs`,
  `src/Server/EventStore.fs` (UTC append timestamp), `src/Client/Pages/BookDetail/State.fs`
  (`Set_book_status`), `src/Client/Components/EditableDateInput.fs` — the code sites this ADR
  reasons about.
