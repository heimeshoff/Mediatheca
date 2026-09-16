---
id: curation-w9fkq
title: Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved)
status: backlog
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
