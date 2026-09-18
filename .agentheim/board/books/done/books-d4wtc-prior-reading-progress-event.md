---
id: books-d4wtc
title: Prior reading progress — the bulk import's `Record_prior_reading_progress` command yields a `Prior_reading_progress_recorded` event for a book its source has not reported on before (never InFocus-promoting, finishing with its own date; the nightly sync and Manual keep producing ordinary observations), projected as `kind = 'prior'` so the History list, the Hours Listened stat and any future Reading day never mistake an import's starting position for a listening session
status: done
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
3. New command `Record_prior_reading_progress of ReadingProgressObservedData` on `BookCommand`
   (ADR-0082 §2, builder ruling 2026-09-18: a prior exists only for the bulk "Import library" run;
   the aggregate must not infer it from state). `decide`: if `book.Observations |> Map.exists (fun
   (_, src) _ -> src = data.Source)` is true, handle it exactly as `Observe_reading_progress data`
   (factor the existing branch into a shared helper); otherwise validate percent/date as the
   observation branch does, then emit `Prior_reading_progress_recorded data`, plus
   `Book_status_changed (Finished, Some data.ObservedOn)` when `data.Percent = 100 ||
   data.Finished` (ADR-0077 §5) — a first-ever 0 % prior is recorded, never dropped as `Ok []`, and
   a prior never emits `Book_status_changed (InFocus, _)`; the next raising observation promotes.
   `Observe_reading_progress` itself is **unchanged**: the nightly sync and the Manual popover keep
   producing `Reading_progress_observed` even for a book with no prior entry — a title first seen
   by a daily run is a normal History row dated to that run.
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
- [ ] `Books.decide`: zero Audible entries in `Observations` + `Record_prior_reading_progress { Source = Audible; Percent = 0 }` → `[Prior_reading_progress_recorded data]`, not `Ok []`.
- [ ] Same book, `Percent = 100` (or `Finished = true`) on that first prior → `[Prior_reading_progress_recorded data; Book_status_changed (Finished, Some data.ObservedOn)]`.
- [ ] A prior never emits `Book_status_changed (InFocus, _)`, regardless of percent or current status.
- [ ] `Record_prior_reading_progress` on a book whose source already has an entry behaves exactly like `Observe_reading_progress` (no-op on same percent, `Reading_progress_observed` + InFocus promotion on a raise), never a second prior.
- [ ] `Observe_reading_progress` with zero entries for its source (Audible or Manual) emits `Reading_progress_observed` — never a prior; a raise from the 0 baseline still promotes to InFocus as today.
- [ ] Removing a book's only prior for a source (`Remove_reading_progress_observation`) empties that source's entries, so a following `Record_prior_reading_progress` records a fresh prior while a following `Observe_reading_progress` records an ordinary observation.
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
- `AudibleSync.fs` / `Api.fs` call sites are untouched here; `integration-dtdbb` makes "Import
  library" issue `Record_prior_reading_progress`, wires the last-listened date and the legacy
  repair. Until it lands, nothing issues the new command, so behaviour is unchanged end to end.
- Refined 2026-09-18 (builder): the first draft let the aggregate infer a prior from "first entry
  per source on any command". Dropped — a book first seen by the nightly sync with yesterday's
  listening is a real listening day; only the bulk import is a prior, so the command carries the
  intent (ADR-0082 §2, Alternatives).

## Verifier note (iteration 1)

**VERDICT: FAIL** (2026-09-18 01:52)

**REASONS:**
- `src/Server/BookProjection.fs` `getReadingStats` (`AND bl.progress_kind <> 'prior'`) is NULL-unsafe against the migration path the same diff ships. The new `book_list`/`book_detail` column is added as nullable with no backfill (`ALTER TABLE book_list ADD COLUMN progress_kind TEXT`), while `book_progress.kind` correctly gets `NOT NULL DEFAULT 'observation'`. On any pre-existing database every `book_list.progress_kind` is therefore NULL, `NULL <> 'prior'` is NULL, and the row is filtered out — so `getReadingStats().HoursListenedThisYear` silently returns `None` for the whole library after deploy. Boot never rebuilds (`Composition.fs`: a full rebuild is an explicit operator command), so this persists until an operator rebuilds the Books projection. The same asymmetry guarantees false drift in the shadow-replay drift check (live NULL vs. rebuilt `'observation'`/`'prior'`) for every book with progress. The two new projection tests for `HoursListenedThisYear` run against a freshly-created DB and cannot see this; acceptance criterion 8 is satisfied only on the fresh-DB path.
- Check 5: the `README_DELTA` block updates only the Books README's `## Ubiquitous language`. The diff adds a new domain event and a new command, but the BC README's `## Key events` and `## Key commands` lists — which enumerate exactly these names — get no op, so both go stale the moment this integrates (`Prior_reading_progress_recorded` missing from Key events, `Record_prior_reading_progress` missing from Key commands).

Everything else audited clean: build green; Expecto 922 passed (baseline 910, +12); Vitest 113. `decideObserveProgress` is a faithful behaviour-preserving extraction; the prior branch uses `Map.exists` over the source, records a first-ever 0 % prior, never emits InFocus, emits Finished with `Some data.ObservedOn`; evolve/serialization/handledEventTypes/EventFormatting correct, no upcast; the `try ALTER TABLE … with _ -> ()` idiom matches GameProjection.fs; scope clean; ADR-0082 §§1-7 honored. [human-eye] criterion pending.

**SUGGESTED_FIX:** Make the kind filter NULL-tolerant and/or backfill the new column — e.g. `AND COALESCE(bl.progress_kind, 'observation') <> 'prior'` in `getReadingStats`, or follow the `ALTER TABLE` arms with `UPDATE book_list/book_detail SET progress_kind = 'observation' WHERE progress_kind IS NULL` — and add a projection test that exercises the migrated-DB shape (pre-existing rows with NULL `progress_kind` still counted). Also add `README_DELTA` ops appending `Prior_reading_progress_recorded` to the Books README's `## Key events` and `Record_prior_reading_progress` to `## Key commands`.

**ITERATION_HINT:** likely-fixable

## Outcome

Implemented ADR-0082 end to end on the aggregate/projection/DTO/rendering side (the import wiring stays `integration-dtdbb`'s job). Iteration 2 fixes the two verifier-flagged gaps from iteration 1: a NULL-unsafe migration path in `getReadingStats`, and two stale BC README sections.

- `src/Server/Books.fs`: `Prior_reading_progress_recorded of ReadingProgressObservedData` (event, reuses the observation payload verbatim) and `Record_prior_reading_progress of ReadingProgressObservedData` (command). `evolve` seeds `Observations` for the new event identically to `Reading_progress_observed`. `decide`'s observation logic was factored into a private `decideObserveProgress` helper, shared by `Observe_reading_progress` (unchanged behaviour) and by `Record_prior_reading_progress` once the source already has an entry (ADR-0082 §2: "behaves exactly as `Observe_reading_progress`"). A first-ever prior — including at 0 % — is always recorded (never `Ok []`), never emits `Book_status_changed (InFocus, _)`, and emits `Book_status_changed (Finished, Some data.ObservedOn)` at 100 % or the `Finished` flag. Serialization (`encodeReadingProgressObservedData`/`decodeReadingProgressObservedData` reused), `handledEventTypes`, and `EventFormatting.formatBookEvent` all gained the new event type.
- `src/Server/BookProjection.fs`: `book_progress` gained a `kind TEXT NOT NULL DEFAULT 'observation'` column (plus an `ALTER TABLE ... ADD COLUMN` migration arm mirroring `GameProjection.fs`'s idiom, for databases that already have the table); `book_list`/`book_detail` gained `progress_kind TEXT`. The `Reading_progress_observed` handler's INSERT now sets `kind = 'observation'` explicitly; a new `Prior_reading_progress_recorded` arm runs the identical INSERT with `kind = 'prior'`, then `recomputeProgress`, which now also carries `kind` through to `book_list`/`book_detail`. `getProgressHistory` selects `kind` and populates `ReadingProgressDto.Kind`.
- **Iteration 2 fix 1 (verifier reason 1):** `getReadingStats`'s `HoursListenedThisYear` filter is now `AND COALESCE(bl.progress_kind, 'observation') <> 'prior'` — NULL-tolerant, so a pre-existing (pre-migration) row is never silently dropped. Additionally, `handler.Init` now backfills `book_list.progress_kind`/`book_detail.progress_kind` for any row left NULL by the `ALTER TABLE ... ADD COLUMN progress_kind TEXT` arms, deriving the value from that book's own latest `book_progress` row using the exact same `ORDER BY` as `recomputeProgress` — the drift-honest choice, since a rebuilt projection would compute the identical value. This closes the shadow-replay drift risk the verifier flagged (ADR-0031), not just the query-level symptom. New Expecto test `"Init against a pre-migration book_list/book_detail/book_progress schema backfills progress_kind and keeps counting hours"` (`tests/Server.Tests/BookProjectionTests.fs`) pre-creates the exact pre-task table shapes (no `progress_kind`/`kind` columns), inserts a Finished/Audible/100% row + matching `book_progress` row the old way, runs `handler.Init`, and asserts both that `book_list.progress_kind` is backfilled to `'observation'` (not NULL) and that `getReadingStats().HoursListenedThisYear` still counts it — mirroring the existing legacy-`goodreads_book_id`-column test's pre-create-then-Init pattern.
- **Iteration 2 fix 2 (verifier reason 2):** `README_DELTA` now also appends `Prior_reading_progress_recorded` to the Books README's `## Key events` section and `Record_prior_reading_progress` to its `## Key commands` section (both reported above), alongside the iteration-1 Ubiquitous-language/Journal deltas.
- `src/Shared/Shared.fs`: new `ProgressKind = Observed | Prior` and `ReadingProgressDto.Kind: ProgressKind`.
- `src/Client/Pages/BookDetail/Views.fs`: `progressHistoryRow` renders a `Kind = Prior` row at `opacity-50` with "starting position" in place of the source badge.
- Tests: `tests/Server.Tests/BooksTests.fs` gained 7 new `decide`/`evolve` cases covering every aggregate acceptance criterion. `tests/Server.Tests/BookProjectionTests.fs` gained 6 new cases total (5 from iteration 1, plus iteration 2's migrated-DB backfill test) covering `kind` on `book_progress` rows, `progress_kind` denormalization, zero `kind = 'prior'` rows from a legacy-only event log, `Kind` round-tripping through `getBySlug`, `HoursListenedThisYear` excluding/including a prior/observation latest row, and the migrated-schema backfill.
- `npm run build`, Expecto (923 passing, up from the iteration-1 baseline of 922), and Vitest (113 passing) are all green.
- No upcast of historical events — legacy `Reading_progress_observed` events replay exactly as before, producing zero `kind = 'prior'` rows (explicitly tested).
- README deltas reported above (Books' **Prior reading progress** UL entry, Key events, Key commands; Journal's **Reading day** entry restricted to `kind = 'observation'` rows).
- `AudibleSync.fs`/`Api.fs` remain untouched, as scoped — nothing issues `Record_prior_reading_progress` yet; that's `integration-dtdbb`.
