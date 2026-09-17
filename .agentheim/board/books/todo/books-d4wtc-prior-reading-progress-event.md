---
id: books-d4wtc
title: Prior reading progress — a source's first-ever position report for a book is a `Prior_reading_progress_recorded` event (never InFocus-promoting, finishing with its own date), projected as `kind = 'prior'` so the History list, the Hours Listened stat and any future Reading day never mistake an import's starting position for a listening session
status: todo
type: feature
context: books
created: 2026-09-18
completed:
depends_on: []
blocks: [integration-dtdbb]
tags: [books, audible, reading-progress, prior, import, finished-date]
related_adrs: [0082, 0076, 0077, 0043]
related_research: [audible-finished-and-last-listened-timestamps-2026-09-18, audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [books-y9kxy, books-f33e2, books-jm7aa]
---

## Why
Audible import backdates nothing today: every title in a first-time library import gets a
`Reading_progress_observed` dated to the moment of import, at its full historical position (e.g.
"100% · 600 of 600 min"), indistinguishable from a same-day marathon listening session, and every
already-finished title silently gets `finished_at = today` for a book actually finished months or
years ago. The Books aggregate has no way to say "this is where the reader already was" versus
"this is what they read today". This task gives it that distinction (ADR-0082).

The nightly sync is already correct in substance: events store absolute positions, the aggregate
appends only when the percent moved, and "minutes listened since last sync" is the difference
between consecutive same-source rows. What is missing is that the FIRST row per source has no
predecessor and must not be read as a session.

## What
1. `src/Server/Books.fs`: add `Prior_reading_progress_recorded of ReadingProgressObservedData` to
   `BookEvent` (reuse the existing record type verbatim). Ubiquitous-language term: **Prior reading
   progress** — a source's first-ever reported position for a book; where the reader already was,
   not a session read that day. (Named after Games' `Prior_play_time_recorded`; "baseline" is
   already taken by `latestPercentForSource` / "Progress regression".)
2. `evolve`: `Active book, Prior_reading_progress_recorded data -> Active { book with Observations =
   book.Observations |> Map.add (data.ObservedOn, data.Source) data.Percent }` — identical seeding
   to `Reading_progress_observed`'s arm, so the per-source no-op / regression rules see it.
3. `decide`'s `Observe_reading_progress` branch: **before** the existing per-source no-op comparison,
   test `data.Source <> ProgressSource.Manual && not (book.Observations |> Map.exists (fun (_, src)
   _ -> src = data.Source))`. When true, emit `Prior_reading_progress_recorded data`, plus
   `Book_status_changed (Finished, Some data.ObservedOn)` when `data.Percent = 100 ||
   data.Finished` (ADR-0077 §5), and skip the no-op/promotion path entirely — a first-ever 0 %
   report must still be recorded, never dropped as `Ok []`. A prior never emits
   `Book_status_changed (InFocus, _)`; the next raising observation promotes. Manual, or a source
   that already has an entry, is unchanged.
4. Serialization: reuse `encodeReadingProgressObservedData` / `decodeReadingProgressObservedData`
   for event type `"Prior_reading_progress_recorded"`; add the `serialize` / `deserialize` arms and
   the string to `handledEventTypes` (and `EventFormatting.fs` if it names event types).
5. `src/Server/BookProjection.fs`: `book_progress` gains `kind TEXT NOT NULL DEFAULT 'observation'`;
   the `Reading_progress_observed` handler's INSERT sets `kind = 'observation'` explicitly; a new
   `Prior_reading_progress_recorded` arm runs the identical INSERT with `kind = 'prior'`, then
   `recomputeProgress`. `book_list` / `book_detail` gain `progress_kind TEXT`, populated by
   `recomputeProgress` alongside `progress_percent` / `progress_source` / `progress_observed_on`.
6. `getReadingStats`'s `HoursListenedThisYear` query: add `AND bl.progress_kind <> 'prior'` so a
   fallback-dated prior never books a full runtime as "listened this year".
7. `src/Shared/Shared.fs`: add `ProgressKind = Observed | Prior` and `Kind: ProgressKind` on
   `ReadingProgressDto`; `BookProjection.getBySlug`'s history query selects the new column.
8. `src/Client/Pages/BookDetail/Views.fs` `progressHistoryRow`: when `row.Kind = Prior`, render the
   row distinctly — muted, labelled "starting position" in place of the observation styling.
9. README deltas (reported in RESULT, applied by the conductor): Books UL entry **Prior reading
   progress** (text in Notes); Journal's **Reading day** entry restricted to `kind = 'observation'`
   rows — a prior row never manufactures a reading day.
10. No upcast of historical `Reading_progress_observed` events — the legacy repair is
    `integration-dtdbb`'s import-path concern.

## Acceptance criteria
- [ ] `Books.decide`: zero Audible entries in `Observations` + `Observe_reading_progress { Source = Audible; Percent = 0 }` → `[Prior_reading_progress_recorded data]`, not `Ok []`.
- [ ] Same book, `Percent = 100` (or `Finished = true`) on that first observation → `[Prior_reading_progress_recorded data; Book_status_changed (Finished, Some data.ObservedOn)]`.
- [ ] A prior never emits `Book_status_changed (InFocus, _)`, regardless of percent or prior status.
- [ ] A second `Observe_reading_progress` from the same source emits `Reading_progress_observed` (or the no-op), never another prior.
- [ ] `Observe_reading_progress` with `Source = Manual` and no Manual entry emits `Reading_progress_observed`, never a prior.
- [ ] Removing a book's only prior for a source (`Remove_reading_progress_observation`) empties that source's entries, so the next observation is again classified as a prior.
- [ ] `book_progress` rows from `Prior_reading_progress_recorded` carry `kind = 'prior'`; rows from `Reading_progress_observed` carry `kind = 'observation'`; `book_list.progress_kind` follows the latest row.
- [ ] `getReadingStats().HoursListenedThisYear` excludes a book whose latest Audible row is `kind = 'prior'`.
- [ ] `ReadingProgressDto.Kind` round-trips through `BookProjection.getBySlug` for both kinds.
- [ ] Rebuilding from a log holding only legacy `Reading_progress_observed` events produces zero `kind = 'prior'` rows.
- [ ] `npm run build`, Expecto and Vitest are green.
- [ ] The History list renders a prior row visibly distinct from an observation row (muted, "starting position"). [human-eye]

## Notes
- ADR-0082 (amends ADR-0076 §2 and ADR-0077 §5) is the decision record; ADR-0043's engagement
  test is why the prior is an event at all: it is an observation Audible made on that day, relayed
  late, and the position cannot be re-observed.
- The prior's `ObservedOn` is the day the position was last true according to the source
  (Audible's `last_position_heard.last_updated`, fetched by `integration-dtdbb`), falling back to the
  import day. That is why `finished_at = last-listened day` falls out of ADR-0077 §5 with no new
  field.
- Books README delta (UL, after **Progress observation**): "**Prior reading progress** — a source's
  first-ever reported position for a book: where the reader already was, not a session read that
  day. `Prior_reading_progress_recorded`, same payload as an observation; seeds the per-source
  baseline, never promotes to InFocus, finishes with its own `ObservedOn` when at 100 % or flagged
  finished; dated by the source's own last-known-true day when available, else the day Mediatheca
  learned it. Projected as `book_progress.kind = 'prior'`." Journal README delta (**Reading day**):
  derived from `book_progress` rows with `kind = 'observation'` only.
- Goodreads is being removed (`integration-sfmxg`, in doing/) — `ProgressSource` is `Audible |
  Manual` after it lands; do not design for Goodreads.
- `AudibleSync.fs` / `Api.fs` call sites are untouched here; `integration-dtdbb` wires the
  last-listened date and the legacy repair.
