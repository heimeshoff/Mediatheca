---
id: curation-w9fkq
title: Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved)
status: done
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: [curation-cyxbc, design-system-001-formalize-styleguide]
blocks: []
tags: [catalogs, curation, typed-entries, follow-up, migration, administration]
related_adrs: [0079, 0080, 0032]
related_research: []
prior_art: [curation-cyxbc]
---

## Why

ADR-0079 (typed catalog entries) always intended a catalog entry's identity to be the pair
`(MediaType, slug)`, not the bare slug — but its §5 deliberately kept `catalog_entries`'
`UNIQUE(catalog_slug, movie_slug)` unwidened, because SQLite treats `NULL`s as distinct in a
UNIQUE index, so widening it before every legacy (untyped) row has a real `media_type` would
silently drop duplicate protection for all of them. `curation-cyxbc`'s implementation surfaced the
sharp edge of that deferral: `Catalogs.decide`'s `Add_entry` cannot honor §3's typed-pair
duplicate rule while §5's constraint stays slug-only, so it rejects any same-slug add within one
catalog, typed or not, until this follow-up lands — stricter than this BC's stated language ("a
media item appears at most once per catalog" was meant per-type, not per-slug).

§5 flagged the backfill's shape as open: "a one-off read-time inference written back, or a
corrective event, to be decided then." Refinement (tactical-modeler + architect via the
orchestrator, 2026-09-16) settled it, recorded as the ADR-0079 amendment dated 2026-09-16
(curation-w9fkq): **a corrective event, whose resolution is NOT the read-time join-order guess
written back verbatim.** That guess only ever resolves Movie or Series and defaults everything
else to Movie (`getEntries`' `ELSE COALESCE(ml.name, sl.name, …)` never consults `game_list` or
`book_list`); freezing it would permanently drop a wrongly-typed Game or Book entry out of
`getCatalogsForMedia` / `getEntriesByMediaSlug`'s `media_type = @type OR media_type IS NULL`
fallback and out of its own removal cascade — a real regression, not the "cosmetic, self-healing"
render ADR-0080 §10 already distinguished a corrective event from. The backfill instead uses the
report-don't-guess discipline ADR-0080 §10 established: exact-match resolution against all four
`*_list` tables; ambiguous / orphan slugs reported by name and left untyped.

## What

- **New `Catalogs.CatalogEvent` case** `Entry_media_types_inferred of InferredEntryMediaType list`
  (`{ EntryId: string; MediaType: MediaType }`) — one event per catalog stream, batching that
  catalog's resolved legacy entries (mirrors `Entries_reordered`'s bulk-list shape on this
  aggregate). `evolve` sets `EntryState.MediaType` for each named entry unconditionally
  (last-write-wins, like `Entry_updated`); unknown entry ids are a no-op, matching every other
  arm's tolerance. Round-tripped through `Catalogs.Serialization` (added to `handledEventTypes`);
  decoding an unrecognized `mediaType` string fails the whole decode rather than silently dropping
  that entry — a partially-applied correction is worse than a rejected one.
- **Resolver**: for every `Catalog-*` stream, reconstitute the aggregate; for every entry with
  `MediaType = None`, resolve its slug (using `base_slug` — the part before `:` — for
  series-shaped `slug:suffix` entries, exactly as `CatalogProjection.getEntries` parses) by an
  exact-match probe against `movie_list`, `series_list` (on `base_slug`), `game_list` and
  `book_list`: exactly one match is `resolved`, zero is `orphan`, two or more is `ambiguous`.
- **One `IAdminApi` member** (e.g. `backfillCatalogEntryMediaTypes`) — no separate preview/confirm
  (additive, non-destructive, safely re-runnable), reachable from the Administration UI. It
  appends one `Entry_media_types_inferred` per catalog for that catalog's `resolved` entries only,
  via `EventStore.appendToStream` with the stream's current position as `expectedPosition`
  (ADR-0032's compensating-event path — no new `CatalogCommand`, there is no user intent to
  express), catches projections up the normal way, and returns a report naming every
  `resolved` / `ambiguous` / `orphan` entry by catalog slug + entry slug. Idempotency is decided
  against each catalog's **reconstituted aggregate** (`EntryState.MediaType = None`), never the
  derived projection.
- **`catalog_entries` self-heals its schema at projection `Init`** (every app start, the lifecycle
  point the existing `ALTER TABLE … ADD COLUMN media_type` guard uses — delete that guard): fold
  `media_type TEXT` into the base `CREATE TABLE` and widen to
  `UNIQUE(catalog_slug, media_type, movie_slug)`; when `sqlite_master.sql` for the live table
  still shows the narrow constraint, transactionally recreate it preserving every row (including
  still-`NULL` `media_type`). No manual Rebuild-all is required for the widened constraint to take
  effect, and ordering against the backfill is not load-bearing: SQLite's NULL-distinct UNIQUE
  semantics give untyped rows the same non-protection either way, and `decide`'s legacy-untyped
  branch is the actual guard for them, not the index. This closes the runtime window that would
  otherwise reopen cyxbc's clobber bug between deploy (relaxed `decide` live immediately) and a
  manual Rebuild-all (widened index only then).
- **`Catalogs.decide`'s `Add_entry` arm** restored to ADR-0079 §3's pair-identity wording, using
  `Seq.tryPick` across *all* same-slug entries — the current `Seq.tryHead` is a latent bug once two
  same-slug entries of different types can coexist (it inspects an arbitrary one and may accept a
  true duplicate). Two typed entries of different `MediaType` sharing a slug in the same catalog:
  accepted. Same `MediaType` and slug: rejected. Either side legacy-untyped and matching slug:
  rejected regardless of the other side's type — reword the in-code comment from "the projection
  key is slug-only" (retired reasoning) to "the widened UNIQUE gives untyped rows no duplicate
  protection; this branch is the sole guard for unresolved rows".
- **README delta (reported, applied by the conductor):** see Notes.

## Acceptance criteria

- [ ] Expecto: the resolver's three outcomes on a constructed fixture — exactly one `*_list`
      match resolves; zero matches reports `orphan`; the same slug present in both `movie_list`
      and `book_list` reports `ambiguous` — and a series-shaped `slug:s01e05` entry resolves via
      `base_slug` against `series_list`.
- [ ] Expecto: the resolver genuinely consults `game_list` and `book_list` — a legacy untyped Game
      entry and a legacy untyped Book entry each resolve to their own type (the read-time
      fallback would have called both Movie).
- [ ] Expecto: `Entry_media_types_inferred` round-trips through `Catalogs.Serialization` and is
      in `handledEventTypes`; a decode with an unrecognized `mediaType` string fails the decode.
- [ ] Expecto: `evolve`'s `Entry_media_types_inferred` arm sets `EntryState.MediaType` for every
      named entry, is a no-op for unknown entry ids, and is inert on `Removed` / `Not_created`.
- [ ] Expecto through the admin API: the action appends exactly one `Entry_media_types_inferred`
      per catalog with `resolved` entries, skips catalogs with none, its report names every
      `ambiguous` / `orphan` entry by catalog slug + entry slug, and after projection catch-up
      `getEntries` returns the resolved rows with `media_type` set.
- [ ] Expecto: rerun idempotency — a second invocation appends zero events and reports zero
      resolved; the "still untyped" check is decided from the reconstituted aggregate (a scenario
      where the projection table is dropped and rebuilt mid-way gives the same answer).
- [ ] Expecto: `catalog_entries` self-heals — (a) a fresh DB gets
      `UNIQUE(catalog_slug, media_type, movie_slug)` from `Init`; (b) a DB seeded with the
      old narrow-constraint DDL and rows (some typed, some `NULL`) is recreated with the widened
      constraint on the next `Init`, every row intact, and a second `Init` is a no-op.
- [ ] Expecto (`CatalogsTests.fs`): `Add_entry` accepts a typed Movie and a typed Book sharing a
      slug in the same catalog (cyxbc's rejection case flips to acceptance); a catalog already
      holding both still rejects a second same-type add at that slug (the `tryHead`→`tryPick`
      regression guard); legacy-untyped vs. anything still rejects on slug equality alone.
- [ ] Expecto (`CatalogProjectionTests.fs`): a movie and a book sharing a slug in the SAME catalog
      each resolve to their own row via `getEntries`, and `checkProjectionDrift` reports zero
      discrepancies for the catalog projection after backfill + rebuild.
- [ ] `grep -n "Seq.tryHead" src/Server/Catalogs.fs` is empty; the old
      `ALTER TABLE catalog_entries ADD COLUMN media_type` guard is gone.
- [ ] `npm run build`, `npm test`, `npm run test:client` green.
- [ ] The backfill action is reachable from the Administration UI and renders the
      resolved / ambiguous / orphan counts and names legibly. [human-eye]

## Notes

- **Read ADR-0079 in full first, including its two 2026-09-16 amendments** — the second
  (curation-w9fkq) is the decision record for this task's shape and is already applied; do not
  re-derive it. Also ADR-0080 §10 (report-don't-guess) and ADR-0032 (compensating-event append).
- Ceremony is deliberately lighter than ADR-0080's Gate 1 / Gate 2: nothing is destroyed (no table
  dropped, no event rewritten or deleted), so a single no-preview admin action is enough; ADR-0034
  guardrails (VACUUM INTO, checkpoint rewind) do not apply to a plain append.
- Workers and tests run against fixtures only; the builder runs the backfill on harbour after
  deploy and hand-fixes any ambiguous / orphan entries through the existing ADR-0032
  compensating-event composer.
- Live data (dev copy, 2026-09-16): small. Same-slug-across-two-`*_list`-tables collisions are
  possible in principle (all four types slug as `slugify(title)-year`) but not measured.
- **README delta** (`.agentheim/knowledge/contexts/curation/README.md`):
  - Replace the **Catalog entry** bullet with: "**Catalog entry** — one item in a catalog.
    Identity is `(MediaType, slug)`: a catalog may hold two entries of different media types
    sharing a slug (ADR-0079, restored to its original pair-identity wording by curation-w9fkq's
    `media_type` backfill and widened uniqueness constraint). Entries recorded before entries were
    typed carry no MediaType; an *inferred media type* is recorded for each by a one-off
    Administration-triggered backfill where resolvable, leaving genuinely ambiguous or orphaned
    slugs untyped and reported by name. Has a position."
  - Add after it: "**Inferred media type** — a `MediaType` recorded for a legacy (untyped) catalog
    entry by an exact-match resolution against exactly one of `movie_list` / `series_list` /
    `game_list` / `book_list`; appended as the corrective event `Entry_media_types_inferred`, never
    guessed — slugs matching zero or more than one list are reported, not converted
    (curation-w9fkq)."
  - **Key events:** append `Entry_media_types_inferred` (no corresponding command — appended
    directly by an Administration action, not through `decide`).
- Open, non-blocking: which Administration tab hosts the button is the worker's call. The
  styleguide dependency is done; it is listed to satisfy the frontend gate for that one button.

## Verifier note (iteration 1)

**REASONS:**
- Acceptance criterion "Expecto (`CatalogProjectionTests.fs`): a movie and a book sharing a slug in the SAME catalog each resolve to their own row via `getEntries`, **and `checkProjectionDrift` reports zero discrepancies for the catalog projection after backfill + rebuild**" is only half covered. The first conjunct is covered. The second has no test anywhere: no test added by this diff calls `Administration.checkProjectionDrift`, and the only pre-existing catalog-related drift coverage (`tests/Server.Tests/ProjectionDriftTests.fs:179`, an unmodified table list) never exercises the `Entry_media_types_inferred` backfill or the `Init`-time table rename/recreate. This is exactly where drift could open: `createTables` now rewrites the live `catalog_entries` table (rename + recreate + copy) while a shadow rebuild builds it fresh, and `handleEvent`'s new `UPDATE catalog_entries SET media_type` arm is the only thing that reproduces the backfilled column on replay — neither is asserted equal to a rebuild.
- Additional defect: `Entry_media_types_inferred` was added to `Catalogs.Serialization.handledEventTypes` (`src/Server/Catalogs.fs:411`) but has no arm in `EventFormatting.formatCatalogEvent` (`src/Server/EventFormatting.fs:412-433`, which ends at `Entries_reordered` then `| _ -> None`). This breaks the handled-implies-formattable convention this codebase closed and tests per new event type (`tests/Server.Tests/AdministrationTests.fs:484-554, 583-614`: administration-qk3f7 / games-a7dqx / books-y9kxy). The moment the builder runs the backfill, `getHealthStats`' `UnformattableEventTypes` will flag `Entry_media_types_inferred` (`src/Server/Administration.fs:255`) and the Event Browser stream drill-in will render nothing for the corrective event — the very surface `AdminHealth/Views.fs`' own comment points an operator at for hand-fixing ambiguous/orphan entries.
- Unverified (ADR-0062): `npm run build` and `npm test` (952/0/0) were run by the verifier from the worktree and green. `npm run test:client` could not be run — `vitest` was not resolvable at verification time (the main tree's `node_modules` had been emptied by a conductor mistake — a sibling worktree was removed with `--force` before its `node_modules` junction was unlinked; restored with `npm ci` before the iteration-2 re-dispatch). The client diff (AdminHealth Types/State/Views) has no `*.test.fs` counterpart in any case and compiles under `npm run build`.

**SUGGESTED_FIX:** Add the missing drift test to `tests/Server.Tests/CatalogProjectionTests.fs` — run the media-type backfill, then assert `Administration.checkProjectionDrift live shadow [ CatalogProjection.handler ]` returns zero discrepancies (and ideally that it still does after a `rebuildProjectionWithProgress`). Also add an `Entry_media_types_inferred` arm to `EventFormatting.formatCatalogEvent` with an `AdministrationTests` case asserting it appears in neither `UnhandledEventTypes` nor `UnformattableEventTypes`, matching the games-a7dqx precedent.

**ITERATION_HINT:** likely-fixable

## Merge-conflict note (iteration 2)

**Sibling:** books-f3sb2 — Book detail page joins catalogs (pill row + picker via `getCatalogsForBook`, `removeBook` catalog cascade, `CatalogManager` extracted to `Components/`). Integrated on `main` as `d81eaab` while this task was iterating; it appended two Expecto cases to `tests/Server.Tests/CatalogProjectionTests.fs`.
**New base SHA:** `5cb92b30b406db2d15d510f9d0de89d33dec369e` (`main` at the time of the merge, incl. books-f3sb2 and the curation-knqfj batch-start).
**Resolution allow-list:** `tests/Server.Tests/CatalogProjectionTests.fs`
**Sibling's `git log -1 --stat main` scoped to the allow-list:**
```
d81eaab feature(books): Book detail page joins catalogs [books-f3sb2]
 tests/Server.Tests/CatalogProjectionTests.fs | 76 ++++++++++++++++++++++++++++
 1 file changed, 76 insertions(+)
```
**Conductor note:** the conflict was detected by a read-only `git merge-tree` probe after the iteration-2 wip checkpoint, before the iteration-2 verification ran; the ladder (salvage tagged `merge-conflict` → `git merge main` in the worktree → resolve-dispatch) was run first so the mandatory post-conflict re-verify (rung 6, two-dot diff against `main`) is the iteration-2 verification, rather than verifying twice. Salvage patch: `C:\src\heimeshoff\containers\mediatheca\.agentheim\salvage\curation-w9fkq-merge-conflict.patch`.

## Outcome

Resolved ADR-0079 §5 (the deferred backfill shape) end to end.

**`Catalogs.fs`:** a new `InferredEntryMediaType = { EntryId; MediaType }` and a new
`CatalogEvent` case `Entry_media_types_inferred of InferredEntryMediaType list` — one event per
catalog stream, batching that catalog's resolved legacy entries (mirrors `Entries_reordered`'s
bulk-list shape). `evolve` sets `EntryState.MediaType` for every named entry unconditionally
(last-write-wins, like `Entry_updated`), no-ops on an unknown entry id, and is inert on
`Removed`/`Not_created` via the existing fallthrough arm. Round-tripped through
`Serialization` (added to `handledEventTypes`); a `mediaType` string the strict decoder doesn't
recognize fails the *whole* decode (`Decode.andThen` + `Decode.fail`), never silently dropping
that one correction. `decide`'s `Add_entry` arm is restored to ADR-0079 §3's original pair-identity
wording: `Seq.tryPick` (not `Seq.tryHead`, which inspected an arbitrary same-slug entry once more
than one can coexist) — two typed entries of different `MediaType` sharing a slug: accepted; same
type and slug: rejected; either side legacy-untyped and matching slug: rejected regardless of the
other side's type, with the in-code comment reworded to explain the widened UNIQUE gives untyped
rows no duplicate protection and this branch is their sole guard.

**`CatalogProjection.fs`:** `catalog_entries`' `media_type` column is folded into the base
`CREATE TABLE` with the widened `UNIQUE(catalog_slug, media_type, movie_slug)`; the old
`ALTER TABLE … ADD COLUMN media_type` guard is gone. A self-heal step runs at every `Init`
(`catalogEntriesNeedsWidening`, reading `sqlite_master`'s stored DDL text): when a live table still
shows the narrow `UNIQUE(catalog_slug, movie_slug)`, it's transactionally renamed, recreated with
the widened constraint, and every row (typed and still-`NULL` alike, including tables that never
even got cyxbc's `media_type` column) is copied across, then the old table dropped and the index
recreated — idempotent, a second `Init` no-ops. `handleEvent` gained the projection arm for
`Entry_media_types_inferred` (`UPDATE catalog_entries SET media_type = … WHERE entry_id = …`,
same unknown-id tolerance as `evolve`). A new `resolveMediaType` (with its `MediaTypeResolution`
DU: `Resolved of MediaType | Ambiguous | Orphan`) does the exact-match probe against
`movie_list`/`series_list` (on `base_slug`, exactly as `getEntries` parses a `slug:suffix`
series-shaped entry)/`game_list`/`book_list` — genuinely consulting all four, unlike the read-time
join-order inference that only ever resolves Movie or Series.

**`Administration.fs`:** a new private `runCatalogMediaTypeBackfill`, wired as the single
`IAdminApi` member `backfillCatalogEntryMediaTypes` (no preview/confirm — additive,
non-destructive, ADR-0032's compensating-event path, bypassing `Catalogs.decide` since there's no
user intent to express). For every `Catalog-*` stream it reconstitutes the aggregate, resolves
every still-untyped entry via `CatalogProjection.resolveMediaType`, and — for catalogs with at
least one resolved entry — appends one `Entry_media_types_inferred` via
`EventStore.appendToStream` at the stream's current position. Catalogs with zero resolved entries
are skipped entirely (no event appended). Projection catch-up runs once at the end, only if
anything was appended. Idempotency is decided from each catalog's **reconstituted aggregate**
(`EntryState.MediaType = None`), never the derived projection — verified against a scenario where
the projection is dropped and rebuilt mid-way between two runs.

**`Shared.fs`:** `CatalogBackfillEntryRef`, `CatalogBackfillResolvedEntry`,
`CatalogMediaTypeBackfillReport` (`Resolved`/`Ambiguous`/`Orphan`, each naming catalog slug +
entry slug) and the `IAdminApi.backfillCatalogEntryMediaTypes` member.

**Client (`Pages/AdminHealth/`):** the action is reachable from the Administration console's
Health tab — a new "Catalog entry media types" card with a "Run backfill" button
(`Run_catalog_media_type_backfill_clicked` / `Catalog_media_type_backfill_completed`) that renders
the resolved/ambiguous/orphan counts and names each ambiguous/orphan entry as
`catalog-slug / entry-slug`.

**Tests (iteration 1):** `CatalogsTests.fs` — the cyxbc-era same-slug-different-type rejection
flips to acceptance; a new `tryHead`→`tryPick` regression guard (a catalog already holding a typed
Movie + Book at one slug still rejects a second same-type add); `evolve`'s
`Entry_media_types_inferred` (sets MediaType, no-ops on unknown ids, last-write-wins, inert on
Removed/Not_created); serialization round-trip + `handledEventTypes` + strict-decode-failure.
`CatalogProjectionTests.fs` — a movie and a book sharing a slug in the SAME catalog each resolve
via `getEntries`; `resolveMediaType`'s three outcomes plus genuine game/book consultation plus
series `base_slug` resolution; the schema self-heal (fresh DB, and a DB seeded with the old
narrow-constraint DDL and rows, some typed some NULL, recreated with every row intact, second
`Init` a no-op). `AdministrationTests.fs` — the admin action appends exactly one event per catalog
with resolved entries and skips catalogs with none; ambiguous entries are reported by name and
never converted; rerun idempotency survives a `Projection.rebuildProjectionWithProgress` in
between.

**Iteration 2 (verifier fixes):** the verifier passed everything above but failed the task on two
specific points, both closed now:

1. **Drift regression test.** Added
   `"backfillCatalogEntryMediaTypes leaves the catalog projection with zero drift, including after
   a full rebuild"` to `AdministrationTests.fs`: seeds two legacy (untyped) entries — one resolving
   to Movie, one to Game — runs `backfillCatalogEntryMediaTypes`, then asserts
   `Administration.checkProjectionDrift conn shadowConn [ CatalogProjection.handler ]` reports zero
   discrepancies (exercising the new `handleEvent` arm for `Entry_media_types_inferred` against a
   shadow replay built from the raw event log). It then runs `Projection.rebuildProjectionWithProgress`
   on the live connection and re-checks drift against a second fresh shadow, confirming the
   self-healing `Init` path (rename + recreate + copy of `catalog_entries`) still reproduces
   identically on a from-scratch rebuild.
2. **`EventFormatting.formatCatalogEvent` arm.** Added the missing `"Entry_media_types_inferred"`
   case (decodes the `items` array's length and reports it as `"{n} entries corrected"`, following
   the neighbouring arms' brevity), plus a new `AdministrationTests.fs` case — `"getHealthStats
   Entry_media_types_inferred appears in neither the unhandled nor the unformattable list"` —
   mirroring the games-a7dqx / books-y9kxy precedent, asserting the event type is absent from both
   `getHealthStats`' `UnhandledEventTypes` and `UnformattableEventTypes`.

`grep -n "Seq.tryHead" src/Server/Catalogs.fs` and `grep -n "ALTER TABLE catalog_entries ADD
COLUMN" src/Server/CatalogProjection.fs` are both empty. `npm run build`, `npm test` (954/954, up
from 952 at iteration 1 — the two new cases above), and `npm run test:client` (103/103, 15 files —
runnable again in this iteration after the conductor restored the main tree's `node_modules`) are
all green.

Key files: `src/Server/Catalogs.fs`, `src/Server/CatalogProjection.fs`,
`src/Server/Administration.fs`, `src/Server/EventFormatting.fs`, `src/Shared/Shared.fs`,
`src/Client/Pages/AdminHealth/{Types,State,Views}.fs`,
`tests/Server.Tests/{CatalogsTests,CatalogProjectionTests,AdministrationTests}.fs`.

**Merge resolution (resolve-conflict dispatch, ADR-0072):** `main` had advanced with sibling task books-f3sb2 (Book detail page catalog integration), which appended two Expecto cases to `tests/Server.Tests/CatalogProjectionTests.fs` — an add/getCatalogsForBook/getCatalog/removeCatalogEntry round-trip, and a `removeBook` cascade test verifying a same-slugged movie's entry survives. Merging surfaced two conflict regions in that file: a testCase-title conflict (my "a movie and a book sharing a slug in the SAME catalog" case vs. the sibling's "adding a book to a catalog is visible via getCatalogsForBook and getCatalog" case) and a large body conflict spanning both my remaining ADR-0079 §5 tests (resolveMediaType and catalog_entries self-heal cases) and the sibling's two new cases. Resolved by keeping every test from both sides in one `testList`, in original relative order (mine first, then the sibling's two book-catalog cases), with no marker, helper, or intent removed from either side. No sibling expectation needed adjustment: both new cases already hold correctly under the restored `(MediaType, slug)` pair-identity `decide` and the widened UNIQUE. Verified via `grep -n "^<<<<<<< \|^=======$\|^>>>>>>> "` (no output) and a full local run: `npm run build` (Fable compiles clean), `npm test` (956/956 Expecto tests pass), `npm run test:client` (108/108 Vitest tests pass).
