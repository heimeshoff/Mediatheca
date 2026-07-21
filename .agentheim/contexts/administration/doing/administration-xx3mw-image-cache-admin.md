---
id: administration-xx3mw
title: Image cache admin — orphan detection, size overview, purge
status: doing
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, images, storage]
related_adrs: [0025]
related_research: []
prior_art: []
---

## Why
The `images/` cache (`<DATA_DIR>/images/` — posters, backdrops, stills, cast photos, journal content images, friend avatars) only ever grows. Slug-changing edits, dropped cast, failed deletes, and edited journal blocks leave orphaned files behind, and nothing reports what the cache holds or reclaims the space. Every image reference the app renders is a typed projection column (verified across all projection files — no markdown-body scanning needed), so orphan detection is a straightforward disk-vs-projections diff — as long as it only runs when the projections it trusts are fully caught up.

## What
(a) **Image cache stats** — total size / file count plus a per-subfolder breakdown (`posters/`, `backdrops/`, `stills/`, `cast/`, `content/`, `friends/`, plus `(root)` for any stray loose file), always available (no guard needed — pure disk footprint).

(b) **Orphan detection** — collect every image ref from the fifteen ref-bearing projection columns into one `Set<string>`, diff against files walked recursively under `images/`, list unreferenced files with relative path + size. Blocked with an operator-facing reason while any of the six checkpoint-tracked projections is dirty or mid-rebuild. The ref-bearing columns:
- `movie_list.poster_ref`, `movie_detail.poster_ref`, `movie_detail.backdrop_ref`
- `series_list.poster_ref`, `series_detail.poster_ref`, `series_detail.backdrop_ref`, `series_seasons.poster_ref`, `series_episodes.still_ref`
- `game_list.cover_ref`, `game_detail.cover_ref`, `game_detail.backdrop_ref`
- `friend_list.image_ref`
- `content_blocks.image_ref` (movie journal), `game_journal_blocks.image_ref` (game journal)
- `cast_members.image_ref`

(c) **Purge** — delete a selected subset or all currently-detected orphans via `ImageStore.deleteImage` (hard `File.Delete`, no trash/backup, filesystem-only, event store never touched). Confirm dialog shows count + total size before commit. The server re-checks the not-dirty guard and re-derives the referenced/orphan sets at commit time, deleting only files still genuinely orphan — anything that became referenced or already vanished between scan and confirm is skipped and reported, never wrongly deleted.

Its own tab at `/admin/images` (`Router.AdminTab`), sibling to Events/Projections/Health/Jobs/Surgery. The Health tab's existing lightweight `images/` size line is untouched.

## Acceptance criteria
- [ ] `/admin/images` renders as a sixth Admin tab, URL-addressable, with a subfolder breakdown table (posters/backdrops/stills/cast/content/friends, plus "(root)" if loose files exist) whose rows sum exactly to the displayed total. Health tab's existing `images/` line is unchanged.
- [ ] The not-dirty guard blocks both orphan listing and purge while any of the six checkpoint-tracked projections is dirty or rebuilding; the client renders the block reason from the DU result, not an error. Stats remain available while blocked.
- [ ] A `content/` journal image referenced by `content_blocks.image_ref` **or** `game_journal_blocks.image_ref` is never flagged orphan.
- [ ] A still referenced via `series_episodes.still_ref` (e.g. `stills/<slug>-s01e02.jpg`) is never flagged orphan.
- [ ] A `cast/<id>.jpg` shared by multiple movies/series is not flagged while any `cast_members` row references it, and is flagged once no row does.
- [ ] Path comparison is separator-normalized and case-sensitive ordinal — a Windows-returned `\`-separated path still matches its forward-slash ref; a case-mismatched name is treated as orphan (matching Linux-deploy semantics).
- [ ] A genuinely unreferenced file, including a stray non-image file, appears in the orphan list with correct relative path and byte size.
- [ ] Selecting a subset for purge deletes exactly that subset and nothing else; selecting "all" deletes every currently-detected orphan.
- [ ] Purge re-derives the referenced/orphan sets at commit: a path that became referenced or already vanished between scan and confirm is skipped (reported in the result), never deleted.
- [ ] The confirm dialog shows accurate count + total bytes (from the held scan) before the purge call commits.
- [ ] Purge returns actual deleted count and bytes freed; re-running stats afterward reflects the smaller total and file count.
- [ ] Purge is filesystem-only — event count is unchanged across a purge (assert in a test).

## Notes
See **ADR-0025** for the ref-source / guard / purge-safety rationale.

- **Registry**: `imageRefColumns : (string * string) list` — new module-level list in `Administration.fs` next to `projectionTables` / `boundedContextPrefixes`, all 15 `(table, column)` pairs above. Guard each query with a `sqlite_master` existence check — `cast_members` and `game_journal_blocks` aren't in `projectionTables` and may not exist in minimal/test fixtures. Doc-comment must flag this registry as load-bearing: a missed column causes wrongful deletion of live images, not just an undercount. Add a coverage test.
- **Guard**: `Administration.isAnyProjectionDirty conn projectionHandlers : string list` (dirty projection names; empty = clean), built from the existing `rebuildingProjections: ConcurrentDictionary<string,unit>` and `Projection.getCheckpointInfo` / `getMaxGlobalPosition` (same `Lag` computation `buildProjectionStats` already does — consider extracting so both callers share it). Only the six handlers registered in `Composition.fs`'s `projectionHandlers` list are checked; `cast_members` / `game_journal_blocks` are imperative writes and never lag/rebuild.
- **On-disk walk**: `Directory.GetFiles(imagesDir, "*", SearchOption.AllDirectories)` (same call `directoryStats` uses), `Path.GetRelativePath(imagesDir, f).Replace('\\','/')` for comparison. No temp/partial-write trap — `ImageStore.saveImage` writes directly to the final deterministic path, no `.tmp` sidecar. WAL/journal files live in `DATA_DIR` root next to `mediatheca.db`, not under `images/` — moot.
- **IAdminApi additions** (`src/Shared/Shared.fs`):
  ```fsharp
  getImageCacheStats: unit -> Async<ImageCacheStats>
  listOrphanedImages: unit -> Async<OrphanScan>
  purgeOrphanedImages: PurgeSelection -> Async<PurgeResult>
  ```
  ```fsharp
  type ImageSubfolderStat = { Subfolder: string; FileCount: int; SizeBytes: int64 }
  type ImageCacheStats = { TotalBytes: int64; TotalFileCount: int; Subfolders: ImageSubfolderStat list }
  type OrphanImage = { RelativePath: string; Subfolder: string; SizeBytes: int64 }
  type OrphanScan =
      | OrphanScanBlocked of reason: string
      | OrphanScanReady of orphans: OrphanImage list * totalBytes: int64
  type PurgeSelection =
      | PurgeAll
      | PurgeSpecific of relativePaths: string list
  type PurgeResult =
      | PurgeBlocked of reason: string
      | PurgeDone of deletedCount: int * bytesFreed: int64 * skipped: string list
  ```
  Guard is signalled via these DU result types (`OrphanScanBlocked` / `PurgeBlocked` with an operator-facing reason string), not exceptions — a DU is idiomatic and serializable, matching the rebuild SSE's `rejected` framing.
- **Purge implementation**: read `FileInfo(f).Length` before each `ImageStore.deleteImage` call to accumulate `bytesFreed`. Reuse `ImageStore.deleteImage` verbatim — do not reimplement `File.Delete`.
- **Client**: new `src/Client/Pages/AdminImages/{Types,State,Views}.fs`, `Router.AdminTab` gets an `AdminImages` case wired through `Route.parseUrl` / `toUrl` / `adminTabSegment`. Frontend task gate applies (ADR-0015 / styleguide — dependency already met).
- **Test fixture**: `tests/Server.Tests/AdministrationTests.fs:274-282` already sets up an `imagesDir` with loose files — extend it for guard / orphan / purge / shared-cast / re-derive cases.
- **README**: add "Images" to the Admin console tab list in `README.md`, plus ubiquitous-language entries for Images tab, Orphaned image, Referenced-ref set, Not-dirty guard, Cache purge.
- **On-disk subfolder facts** (verified): actual set is `posters/ backdrops/ stills/ cast/ content/ friends/`. Game covers live under `posters/` as `posters/game-<slug>.jpg` — there is no `covers/` folder despite the column name `cover_ref`, and no `avatars/` folder.
- **Out of scope**: pruning now-empty subfolder directories after purge (harmless either way; `GetFiles` ignores empty dirs).
