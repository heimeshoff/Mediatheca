---
id: integration-r4vzm
title: Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0070)
status: backlog
type: feature
context: integration
created: 2026-09-10
completed:
depends_on: [integration-qb7tk]
blocks: [integration-mqsd3]
tags: [jellyfin, qbittorrent, sync, movies, series, storage]
related_adrs: [0011, 0043, 0070]
related_research: []
prior_art: [integration-002, integration-m4k7p, integration-001]
---

## Why

"Remove local copy" (integration-mqsd3) is Integration's first destructive cross-system
action. Everything that can go wrong — deleting the wrong torrent, orphaning a seeding
torrent, losing play state Jellyfin discards with the item, clearing a Jellyfin id before
the item is actually gone — is a server-side concern that must be unit-tested with plain
lambdas before any button exists. This task ships the whole flow behind two API members,
verifiable by tests and `curl`, so the UI task is pure client work.

## What

Two `IMediathecaApi` members over a **plan-then-execute** split (ADR-0070):

- `planLocalCopyRemoval: LocalCopyTarget -> Async<Result<RemovalPlan, string>>`
- `removeLocalCopy: LocalCopyTarget * acknowledgedHashes: string list -> Async<RemovalOutcome>`

`LocalCopyTarget = MovieTarget of slug | SeriesTarget of slug` (single-episode and season
targets are follow-ups: no season id is stored, and the episode row has no action surface).

### The plan

`plan` resolves the Jellyfin item (`GET /Items/{id}` → `Path`), maps the mount prefix, and
matches live torrents into a `RemovalPlan`:

- `Path: string`, `Case: RemovalCase`, `Torrents: PlannedTorrent list`
- `RemovalCase = TorrentsPresent | FilesOnly | AlreadyGoneInJellyfin | PathOutsideMountMap`
- `PlannedTorrent = { Hash; Name; Ratio; SeedingTime; Match: MatchKind; Risk: SeedRisk;
  PreTicked: bool }` with `MatchKind = ExactOrInside | PackAncestor of extraFileCount: int`
  and `SeedRisk = Safe | HitAndRun of reason: string`
- `PreTicked` is `false` whenever `Risk = HitAndRun _` or `Match = PackAncestor _`.

Matching, all pure in `src/Server/LocalCopyRemoval.fs`:

- `mapJellyfinPath (jellyfinRoot, qbittorrentRoot) path` — prefix swap. Roots come from env
  vars `JELLYFIN_MEDIA_ROOT` (default `/media`) and `QBITTORRENT_DOWNLOAD_ROOT` (default
  `/downloads`), read once at startup like `STEAM_DEVICE_NAME`. A path not under the
  Jellyfin root yields `PathOutsideMountMap`, and `removeLocalCopy` **refuses** that case
  outright (a broken deployment assumption must fail loudly, never fall back to a
  Jellyfin-only delete that would orphan a seeding torrent).
- `isAncestorOf a b` — segment-boundary aware: `/x/Dune` is not an ancestor of `/x/Dune 2`.
- `matchTorrents itemPath torrents` — torrent content path `C` vs. mapped item path `P`:
  `C = P` or `isAncestorOf P C` → `ExactOrInside`; `isAncestorOf C P` → `PackAncestor n`,
  where `n` is the number of files in the torrent outside `P` (`Qbittorrent.listFiles`,
  integration-qb7tk). A `P` equal to or above a library root (`<root>/movies`,
  `<root>/shows`) is rejected by `plan` before any effect runs.
- `classifySeedRisk ratio seedingTime` — `HitAndRun` when `ratio < minRatio` **and**
  `seedingTime < minSeedingDays`; named constants `minRatio = 1.0`, `minSeedingDays = 14`
  (builder adjusts to IPTorrents' actual rule). qBittorrent's sentinels are handled:
  `ratio = -1` (infinite) is `Safe`; the 9999 cap is `Safe`.

### The execution

`execute` is the orchestrator over an injected effects record — `preserveWatchHistory`,
`listTorrents`, `deleteTorrents`, `deleteJellyfinItem`, `fetchItem`, `clearLinks` — plus
the acknowledged hashes. Fixed order, each step idempotent, returning
`RemovalOutcome = { Completed: RemovalStep list; Failure: (RemovalStep * string) option }`
with `RemovalStep = PreserveWatchHistory | ResolvePath | MatchTorrents | DeleteTorrents |
DeleteJellyfinItem | Verify | ClearLinks`:

1. **PreserveWatchHistory** — import this item's Jellyfin play state through the existing
   event-producing paths (`Movies.Record_watch_session` via a new pure
   `JellyfinImport.syncMovieWatchHistory` seam extracted from `runJellyfinImport`;
   `Series.Mark_episode_watched` via the existing `syncSeriesWatchHistory`). Doctrinally
   mandatory (ADR-0043 / ADR-0070): Jellyfin discards the item's user data with the item.
2. **ResolvePath** / 3. **MatchTorrents** — rebuild the plan from a **fresh**
   `torrents/info`; the matched hash set must equal the acknowledged set, else
   `Failure (MatchTorrents, "the torrent list changed since the dialog was shown — reopen
   it")`. This closes the time-of-check/time-of-use window. Every matched torrent must be
   acknowledged: the tick is an acknowledgement, not a selection, because step 5 deletes
   the files under *every* matched torrent regardless.
4. **DeleteTorrents** — `deleteFiles = true`; skipped when the case is `FilesOnly`.
5. **DeleteJellyfinItem** — `DELETE /Items/{id}`; a 404 is `Ok ()` (already gone).
6. **Verify** — `GET /Items/{id}` → 404 and none of the hashes in a fresh `torrents/info`.
7. **ClearLinks** — `JellyfinStore.clearMovieJellyfinId` / `clearSeriesJellyfinId`
   (cascading to the series' `jellyfin_episode` rows). Only after step 6.

**Ordering rationale:** torrents die first, the Jellyfin item last, links clear only after
verify — the Jellyfin item is the *sole carrier* of the `Path` that finds the torrents, so
deleting it first would destroy the ability to re-plan after a qBittorrent failure. In this
order the plan is always re-derivable from the live world: a retry is "call again", never a
saga table or resume token. A torrents-deleted-but-Jellyfin-failed retry re-plans into
`FilesOnly` — the same code path as the already-common "44 folders, 28 torrents" case.

`removeLocalCopy` refuses with a clear message while a Jellyfin sync is in progress
(`JellyfinSync` exposes its in-progress flag), so the sync's `clearAll` + repopulate never
interleaves with step 7.

### Jellyfin adapter additions (`src/Server/Jellyfin.fs`)

- `Path: string option` on `JellyfinBaseItem` (additive; no existing call site changes).
- The private `fetchJsonWithAuth` generalized into `sendWithAuth method url token`, keeping
  the GET wrapper intact so ADR-0011's pinned `JellyfinReauthTests.fs` stay untouched.
- `getItemWithReauth: … -> Async<Result<JellyfinBaseItem option, string>>` (404 → `Ok None`)
  and `deleteItemWithReauth: … -> Async<Result<unit, string>>` (404 → `Ok ()`), both via
  `withReauthRetry`. **No `NotFound` case is added to `FetchError`** — that would break
  `withReauthRetry`'s exhaustive match.

### Store additions (`src/Server/JellyfinStore.fs`)

`getSeriesJellyfinId` (missing today), `clearMovieJellyfinId`, `clearSeriesJellyfinId`
(cascades to episodes), `clearEpisodeJellyfinId`. All `DELETE … WHERE`, hence idempotent.
`clearAll` is never used by the removal flow.

## Acceptance criteria

- [ ] `mapJellyfinPath` is unit-tested with the default roots and custom roots, and yields
      `PathOutsideMountMap` for a path not under the Jellyfin root.
- [ ] `isAncestorOf` is unit-tested at the segment boundary (`/x/Dune` vs `/x/Dune 2`).
- [ ] `matchTorrents` is unit-tested with fixture `TorrentInfo` lists: a movie in its own
      folder (torrent content path is the folder, Jellyfin path is the file), a bare-file
      movie, a multi-torrent series, two cross-seeds on the same folder, a pack torrent
      whose content path is an ancestor of the target (`PackAncestor n` with the right
      `n`), and zero matches (`FilesOnly`).
- [ ] `plan` rejects a target whose mapped path equals or is above a library root — unit-tested.
- [ ] `classifySeedRisk` is unit-tested at both threshold boundaries, for the flagged case,
      and for the `-1` and `9999` sentinels.
- [ ] `PlannedTorrent.PreTicked` is `false` for every `HitAndRun` and every `PackAncestor`
      row and `true` otherwise — unit-tested.
- [ ] `execute` is unit-tested with fixture lambdas: full success in step order; the
      watch-history lambda fires before any delete lambda; a watch-history failure aborts
      before any delete; an acknowledged set that differs from the fresh match set fails at
      `MatchTorrents` with nothing deleted; a torrent-delete failure aborts before the
      Jellyfin delete; a Jellyfin-delete failure (including a second 401 after one re-auth)
      aborts before `clearLinks`; a verify failure after both deletes still does **not** call
      `clearLinks`; `FilesOnly` skips `deleteTorrents`.
- [ ] `execute` refuses while the injected sync-in-progress guard reports true — unit-tested.
- [ ] `Jellyfin.getItemWithReauth` maps 404 to `Ok None` and `deleteItemWithReauth` maps
      404 to `Ok ()`; both are built on `withReauthRetry` (401-then-success lambda pair).
      The existing six `JellyfinReauthTests.fs` tests are unchanged and pass.
- [ ] `JellyfinStore.getSeriesJellyfinId` exists; `clearSeriesJellyfinId` removes the series
      row and all its episode rows; calling any clear twice is a no-op — tested on in-memory
      SQLite.
- [ ] `JellyfinImport.syncMovieWatchHistory` exists as a pure seam; `runJellyfinImport` calls
      it and the existing `JellyfinImportTests.fs` still pass.
- [ ] No new event case is added to `Movies.fs` or `Series.fs` (grep-checkable; ADR-0070).
- [ ] Live on harbour via `curl` against the two API routes: a movie with a Jellyfin id →
      after `removeLocalCopy`, `GET /Items/{id}` is 404, the hash is absent from
      `torrents/info`, `jellyfin_movie` has no row for the slug. A series → every torrent
      under its show folder gone, the series item 404, no `jellyfin_series` /
      `jellyfin_episode` rows for the slug.
- [ ] The movie's files and the show folder are gone from `/mnt/media/files` on harbour
      (the mediatheca container has no mount of it — builder checks over ssh). [human-eye]
- [ ] Play state recorded in Jellyfin after the last sync is visible in Mediatheca after the
      removal — live check with a movie marked played in Jellyfin right before removal.
- [ ] `npm run build` and `npm test` pass.

## Notes

**Refinement decisions (2026-09-10, orchestrator + architect confirmed; edit to override):**

1. Projection-only, no event — ADR-0070; the Journal is uninvolved.
2. Hit-and-run: warn, never refuse; constants, not settings.
3. Season / single-episode targets: follow-ups.
4. Cross-seed and pack torrents: everything listed, everything acknowledged; a pack
   torrent is never silently ticked (it would delete sibling files) and never silently
   excluded (it would keep seeding files step 5 deletes) — the UI task surfaces the
   "also contains N other files" warning distinctly from the ratio warning.
5. qBittorrent session: in memory per operation; no ADR-0011-style persisted retry.
6. Mount roots: env vars with defaults; unmapped path fails loudly.

**Verified on harbour 2026-09-10:** see integration-mqsd3 Notes (mount prefixes, uid 1000,
admin user with delete right, API endpoints, "torrent already gone" is common, Jellyfin
delete semantics, no `/mnt/media/files` mount in the mediatheca container).

**README:** on completion add **Local copy** / **Removal plan** entries to the Integration
ubiquitous language (the worker's README update).
