---
id: 0025
title: Image-cache orphan detection diffs on-disk files against projection refs, guarded by a not-dirty check, and hard-deletes with re-derivation at purge
scope: administration
status: accepted
date: 2026-07-21
supersedes: []
superseded_by: []
related_tasks: [administration-xx3mw]
related_research: []
---

# ADR 0025: Image-cache orphan detection diffs on-disk files against projection refs, guarded by a not-dirty check, and hard-deletes with re-derivation at purge

## Context

The `images/` cache (`<DATA_DIR>/images/`, subfolders `posters/`, `backdrops/`,
`stills/`, `cast/`, `content/`, `friends/` — game covers also live under
`posters/` as `posters/game-<slug>.jpg`; there is no separate `covers/` folder
despite the column name `cover_ref`) only ever grows. Slug-changing edits,
dropped cast, failed deletes, and edited journal blocks leave orphaned files
behind, and nothing reports cache size or reclaims space. administration-xx3mw
adds an `/admin/images` tab: size/count stats with a subfolder breakdown,
orphan detection, and a hard-delete purge (filesystem-only — the event store
is never touched).

Every image ref the app renders is a typed column value, verified by reading
every projection's INSERT/SELECT statements — no markdown-body scanning is
needed. The full ref-bearing set: `movie_list.poster_ref`,
`movie_detail.poster_ref`, `movie_detail.backdrop_ref`; `series_list.poster_ref`,
`series_detail.poster_ref`, `series_detail.backdrop_ref`,
`series_seasons.poster_ref`, `series_episodes.still_ref`; `game_list.cover_ref`,
`game_detail.cover_ref`, `game_detail.backdrop_ref`; `friend_list.image_ref`;
`content_blocks.image_ref` (movie journal); `game_journal_blocks.image_ref`
(game journal); `cast_members.image_ref`.

Two questions needed a deliberate answer:

1. **Source of truth for "which refs are live"** — read the projection columns
   directly, or replay the event log to reconstruct live refs?
2. **How to avoid false-positive orphans** — diffing disk against a stale or
   mid-rebuild read model can flag a genuinely live file as orphan. Two races:
   catch-up lag (a projection briefly behind the event store head after an
   edit) and rebuild (a projection's tables dropped and being replayed, during
   which nearly every ref is transiently absent).

A structural fact shapes both: of the ref-bearing tables, only the six
registered in `Composition.fs`'s `projectionHandlers`
(Movie/Series/Game/Friend/Catalog/ContentBlock) are checkpoint-tracked and
rebuild-managed. `cast_members` (`CastStore.fs`) and `game_journal_blocks`
(`GameJournal.fs`) are written imperatively (synchronous DELETE+INSERT on
save), owned by no `ProjectionHandler` — a rebuild never drops them and they
never lag.

## Decision

### Live refs come from the projection tables, not event replay

A new module-level registry in `Administration.fs`, `imageRefColumns : (string
* string) list` — the fifteen `(table, column)` pairs above — is queried
`SELECT DISTINCT {column} FROM {table} WHERE {column} IS NOT NULL AND
{column} <> ''` (guarded by a `sqlite_master` existence check, since
`cast_members` / `game_journal_blocks` aren't guaranteed present in minimal/test
DBs) into one `Set<string>`. This mirrors the existing `projectionTables` /
`boundedContextPrefixes` registries: admin-console-only knowledge of the
schema, and the only home that uniformly covers the two imperative tables,
which have no projection module to host a per-module helper.

Event replay is rejected: the projections already are the last-write-wins
computation of "which refs are live," and the not-dirty guard (below)
guarantees they're fully caught up when read. Replaying would recompute —
more slowly, and with a second source of truth to keep consistent — what the
read model already materializes, the same reasoning ADR-0021 applied to the
Health tab.

### Path comparison is ordinal and separator-normalized, never case-folded

Refs and on-disk paths are compared after `\` → `/` normalization
(`Path.GetRelativePath(imagesDir, f).Replace('\\','/')`), case-sensitively. On
disk, filenames equal their stored refs byte-for-byte — both are generated
from a lowercased slug by `ImageStore.saveImage`. Case-folding before compare
would risk masking a genuine mismatch on the case-sensitive Linux deploy
target; ordinal comparison is both correct and deploy-faithful.

### The not-dirty guard: no rebuild, zero lag, across the six checkpoint-tracked handlers

`isAnyProjectionDirty` returns the names of dirty projections (empty = clean):
for any of the six `projectionHandlers` it reports the projection if it's a key
in `rebuildingProjections`, **or** its checkpoint position is behind
`EventStore.getMaxGlobalPosition` (`Lag > 0`, the same computation
`buildProjectionStats` already performs). Both building blocks already exist and
are already surfaced per-projection on the Projections tab. `cast_members` and
`game_journal_blocks` need no gating — written synchronously, never dropped by a
rebuild, always consistent.

The guard is checked before computing the orphan list (a dirty state returns
a `Blocked` result naming the reason, not a computed-but-wrong list) and is
**not** required for the stats endpoint, which is a pure disk footprint and
safe regardless of projection state.

### Purge re-checks the guard, re-derives, and intersects before deleting

Purge (a) fails fast (returns `PurgeBlocked`) if the guard trips; (b)
re-derives the live-ref set and re-walks disk; (c) deletes only paths that
are simultaneously in the client's requested selection, absent from the fresh
live-ref set, and still present on disk, via `ImageStore.deleteImage`; (d)
returns the actual deleted count, bytes freed, and any skipped paths. This
closes the TOCTOU gap opened by an operator pausing on the confirm dialog: a
file can only be deleted if it is genuinely unreferenced at the instant of
deletion. No mutex prevents a rebuild from starting mid-purge — under
ADR-0007's single-operator premise, the realistic concurrent writer during a
dialog pause is a scheduled background job (TMDB refresh, Steam sync), whose
appends the lag re-check already catches, not a second human hand.

Purge is a hard delete (`File.Delete` via `ImageStore.deleteImage`) — no
trash or backup folder, filesystem-only, event store untouched.

## Consequences

### Positive
- Reuses `getCheckpointInfo` / `getMaxGlobalPosition` / `rebuildingProjections`
  verbatim — no new concurrency primitive.
- `imageRefColumns` keeps all cross-BC ref knowledge in one admin-owned place,
  covering checkpoint-tracked and imperative tables uniformly.
- The intersection-delete makes purge safe by construction: a stale orphan
  entry can survive a list, but can never be deleted.

### Negative / accepted tradeoff
- `imageRefColumns` must be kept in sync when a ref-bearing column is added or
  renamed. A missed column silently under-counts live refs, which risks
  deleting a live file — the dangerous failure mode, not a cosmetic one.
  Mitigated by a doc-comment pointing at this consequence and a registry-
  coverage test.
- When any of the six projections lags or rebuilds, the Images tab's orphan
  scan/purge refuse — the operator retries once projections catch up. Correct
  over convenient: a wrong orphan list is worse than a momentary "not ready."
- No recovery for an erroneous purge (hard delete, no trash). Mitigated by the
  guard, re-derivation, and a confirm dialog showing count + bytes before
  commit.

## Alternatives considered
- **Event-log replay for live refs** — rejected: recomputes what the
  projections already materialize, scans the whole store, creates a second
  source of truth.
- **`getAllImageRefs` per projection module** — rejected: spreads an admin
  concern across seven domain modules and can't cover `cast_members` /
  `game_journal_blocks`, which have no projection module.
- **Guard on `IsRebuilding` only, ignoring lag** — rejected: misses the
  catch-up-lag race (a just-downloaded image could be flagged orphan while
  its projection is briefly behind head).
- **A mutex blocking rebuild during purge** — rejected as ceremony the
  single-operator model (ADR-0007) doesn't need.
- **Soft delete to a trash folder** — out of scope; the task specifies hard
  delete, and intersection-delete already prevents deleting live files.

## References
- `src/Server/Administration.fs` — `imageRefColumns` (new), `projectionTables`,
  `boundedContextPrefixes`, `rebuildingProjections`, `buildProjectionStats`.
- `src/Server/Projection.fs` — `getCheckpointInfo`;
  `EventStore.getMaxGlobalPosition`.
- `src/Server/ImageStore.fs` — `saveImage`, `deleteImage`, `imageExists`.
- `src/Server/CastStore.fs`, `src/Server/GameJournal.fs` — the two imperative
  ref-bearing tables (no `ProjectionHandler`).
- `src/Server/Composition.fs` — `projectionHandlers` (the six checkpoint-tracked).
- ADR-0021 — read model / filesystem read on demand, no recomputation cache.
- ADR-0024 — `rebuildingProjections`, `IsRebuilding`, rebuild drop+replay.
- ADR-0007 — single-user/single-operator premise the no-mutex reasoning leans on.
