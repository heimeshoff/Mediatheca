---
id: integration-r4vzm
title: Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0071)
status: done
type: feature
context: integration
created: 2026-09-10
completed: 2026-09-10
depends_on: [integration-qb7tk]
blocks: [integration-mqsd3]
tags: [jellyfin, qbittorrent, sync, movies, series, storage]
related_adrs: [0011, 0043, 0071]
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

Two `IMediathecaApi` members over a **plan-then-execute** split (ADR-0071):

- `planLocalCopyRemoval: LocalCopyTarget -> Async<Result<RemovalPlan, string>>`
- `removeLocalCopy: LocalCopyTarget * acknowledgedHashes: string list -> Async<RemovalOutcome>`

`LocalCopyTarget = MovieTarget of slug | SeriesTarget of slug` (single-episode and season
targets are follow-ups: no season id is stored, and the episode row has no action surface).

### The plan

`plan` resolves the Jellyfin item (`GET /Items/{id}` → `Path`), maps the mount prefix,
derives the **deletion scope** (below), and matches live torrents into a `RemovalPlan`:

- `Path: string`, `Case: RemovalCase`, `Torrents: PlannedTorrent list`
- `RemovalCase = TorrentsPresent | FilesOnly | AlreadyGoneInJellyfin | PathOutsideMountMap`
- `PlannedTorrent = { Hash; Name; Ratio; SeedingTime; Match: MatchKind; Risk: SeedRisk;
  PreTicked: bool }` with `MatchKind = ExactOrInside | PackAncestor of extraFileCount: int`
  and `SeedRisk = Safe | HitAndRun of reason: string`
- `PreTicked` is `false` whenever `Risk = HitAndRun _` or `Match = PackAncestor _`.

Matching, all pure in `src/Server/LocalCopyRemoval.fs`:

- `mapJellyfinPath (jellyfinRoot, qbittorrentRoot) path` — prefix swap. Roots come from env
  vars `JELLYFIN_MEDIA_ROOT` (default `/media`) and `QBITTORRENT_DOWNLOAD_ROOT` (default
  `/downloads`), read once in `Composition.fs` with `Environment.GetEnvironmentVariable`
  the way `TMDB_API_KEY`/`STEAM_ID` are — but held in a plain config record handed to the
  API, **not** seeded into `SettingsStore` (a mount prefix is a deployment fact, not a user
  setting; `STEAM_DEVICE_NAME`, the precedent the first pass named, was deleted with
  integration-v0xmv). A path not under the
  Jellyfin root yields `PathOutsideMountMap`, and `removeLocalCopy` **refuses** that case
  outright (a broken deployment assumption must fail loudly, never fall back to a
  Jellyfin-only delete that would orphan a seeding torrent).
- `isAncestorOf a b` — segment-boundary aware: `/x/Dune` is not an ancestor of `/x/Dune 2`.
- `deletionScope target mappedPath` — what Jellyfin will actually remove, which is what the
  torrents are matched against. A series' scope is its `Path` (the show folder). A movie's
  scope is the **parent folder** of its `Path` — Jellyfin deletes a movie's whole folder when
  the movie owns it — unless that parent is a library root (exactly one segment below the
  Jellyfin root, e.g. `/media/movies`), in which case the movie is a bare file in a mixed
  folder and the scope is the file itself. A scope fewer than two segments below the root
  (the root itself, or a library root) is rejected by `plan` before any effect runs — no
  hardcoded `movies`/`shows` names.
- `matchTorrents scope torrents` — torrent content path `C` vs. the mapped deletion scope
  `D`: `C = D` or `isAncestorOf D C` → `ExactOrInside`; `isAncestorOf C D` →
  `PackAncestor n`, where `n` is the number of the torrent's files outside `D`
  (`Qbittorrent.listFiles` names joined onto `SavePath`, integration-qb7tk). The everyday
  movie torrent — one folder, `C = D` — is therefore `ExactOrInside`, never a pack.
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
   `Series.Mark_episode_watched` via the existing `syncSeriesWatchHistory`, fed by
   `getEpisodesWithReauth` before any delete). Doctrinally
   mandatory (ADR-0043 / ADR-0071): Jellyfin discards the item's user data with the item.
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
(`JellyfinSync.getSyncStatus () = SyncInProgress` already reads the flag under the sync
lock; a small `isSyncInProgress` beside it is the injectable guard), so the sync's
`clearAll` + repopulate never interleaves with step 7.

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

- [x] `mapJellyfinPath` is unit-tested with the default roots and custom roots, and yields
      `PathOutsideMountMap` for a path not under the Jellyfin root.
- [x] `isAncestorOf` is unit-tested at the segment boundary (`/x/Dune` vs `/x/Dune 2`).
- [x] `deletionScope` is unit-tested: a movie in its own folder → the folder; a bare-file
      movie directly in a library root → the file; a series → the show folder.
- [x] `matchTorrents` is unit-tested with fixture `TorrentInfo` lists: a movie in its own
      folder (torrent content path is the folder, Jellyfin path is the file →
      `ExactOrInside`), a bare-file movie (`ExactOrInside`), a multi-torrent series (every
      torrent `ExactOrInside`), two cross-seeds on the same folder (both listed), a pack
      torrent whose content path is a strict ancestor of the scope (`PackAncestor n` with
      the right `n`), and zero matches (`FilesOnly`).
- [x] `plan` rejects a target whose deletion scope is the Jellyfin root or a library root
      (fewer than two segments below the root) — unit-tested.
- [x] `classifySeedRisk` is unit-tested at both threshold boundaries, for the flagged case,
      and for the `-1` and `9999` sentinels.
- [x] `PlannedTorrent.PreTicked` is `false` for every `HitAndRun` and every `PackAncestor`
      row and `true` otherwise — unit-tested.
- [x] `execute` is unit-tested with fixture lambdas: full success in step order; the
      watch-history lambda fires before any delete lambda; a watch-history failure aborts
      before any delete; an acknowledged set that differs from the fresh match set fails at
      `MatchTorrents` with nothing deleted; a torrent-delete failure aborts before the
      Jellyfin delete; a Jellyfin-delete failure (including a second 401 after one re-auth)
      aborts before `clearLinks`; a verify failure after both deletes still does **not** call
      `clearLinks`; `FilesOnly` skips `deleteTorrents`.
- [x] `execute` refuses while the injected sync-in-progress guard reports true — unit-tested.
- [x] `Jellyfin.getItemWithReauth` maps 404 to `Ok None` and `deleteItemWithReauth` maps
      404 to `Ok ()`; both are built on `withReauthRetry` (401-then-success lambda pair).
      The existing six `JellyfinReauthTests.fs` tests are unchanged and pass.
- [x] `JellyfinStore.getSeriesJellyfinId` exists; `clearSeriesJellyfinId` removes the series
      row and all its episode rows; calling any clear twice is a no-op — tested on in-memory
      SQLite.
- [x] `JellyfinImport.syncMovieWatchHistory` exists as a pure seam; `runJellyfinImport` calls
      it and the existing `JellyfinImportTests.fs` still pass.
- [x] No new event case is added to `Movies.fs` or `Series.fs` (grep-checkable; ADR-0071).
- [ ] Live on harbour via `curl` against the two API routes (builder-run after deploy — a
      worker or verifier never touches harbour): a movie with a Jellyfin id →
      after `removeLocalCopy`, `GET /Items/{id}` is 404, the hash is absent from
      `torrents/info`, `jellyfin_movie` has no row for the slug. A series → every torrent
      under its show folder gone, the series item 404, no `jellyfin_series` /
      `jellyfin_episode` rows for the slug.
      **Pending the builder's post-deploy check — not performed by this worker.**
- [ ] The movie's files and the show folder are gone from `/mnt/media/files` on harbour
      (the mediatheca container has no mount of it — builder checks over ssh). [human-eye]
      **Pending the builder's post-deploy check — not performed by this worker.**
- [ ] Play state recorded in Jellyfin after the last sync is visible in Mediatheca after the
      removal — live check by the builder with a movie marked played in Jellyfin right
      before removal.
      **Pending the builder's post-deploy check — not performed by this worker.**
- [x] `npm run build` and `npm test` pass.

## Notes

**Refinement decisions (2026-09-10, orchestrator + architect confirmed; edit to override):**

1. Projection-only, no event — ADR-0071; the Journal is uninvolved.
2. Hit-and-run: warn, never refuse; constants, not settings.
3. Season / single-episode targets: follow-ups.
4. Cross-seed and pack torrents: everything listed, everything acknowledged; a pack
   torrent is never silently ticked (it would delete sibling files) and never silently
   excluded (it would keep seeding files step 5 deletes) — the UI task surfaces the
   "also contains N other files" warning distinctly from the ratio warning.
5. qBittorrent session: in memory per operation; no ADR-0011-style persisted retry.
6. Mount roots: env vars with defaults; unmapped path fails loudly.

**Second refinement pass (2026-09-10, after integration-qb7tk shipped; code-grounded, edit
to override):**

7. Deletion scope, not item path, is what torrents match against. The first pass matched
   the torrent content path against the Jellyfin *file* path, which turned the everyday
   movie torrent (one folder, one movie) into a `PackAncestor` with every sidecar counted
   as an "other file" — pre-unticked and warned on every removal. Matching against what
   Jellyfin will delete (the movie's folder when it owns one, the bare file otherwise, the
   show folder for a series) makes the common case `ExactOrInside`. Accepted hazard: a
   hand-made mixed subfolder holding several movies over-approximates the scope, so a
   sibling movie's torrent can appear in the list — visible by name, acknowledged
   explicitly, never silent; the harbour download pipeline never produces that layout.
8. Every seam named in **What** was checked against the code as of integration-qb7tk:
   `Qbittorrent.{withSession,listTorrents,listFiles,deleteTorrents}` and `TorrentInfo`
   exist as described; `JellyfinStore.getSeriesJellyfinId` is genuinely missing;
   `runJellyfinImport` lives in `Api.fs` with the movie watch sync inline (that is the
   seam to extract); `Jellyfin.fetchJsonWithAuth` is a private GET and `FetchError` has
   exactly `Unauthorized | OtherFailure`; `JellyfinReauthTests.fs` holds six tests,
   `JellyfinImportTests.fs` four. The env-var precedent was stale (`STEAM_DEVICE_NAME`
   died with integration-v0xmv) and now points at `Composition.fs`.
9. The two live-on-harbour criteria are builder-run after deploy; a worker never touches
   the live system.

**Verified on harbour 2026-09-10:** see integration-mqsd3 Notes (mount prefixes, uid 1000,
admin user with delete right, API endpoints, "torrent already gone" is common, Jellyfin
delete semantics, no `/mnt/media/files` mount in the mediatheca container).

**README:** on completion add **Local copy** / **Removal plan** entries to the Integration
ubiquitous language (the worker's README update).

## Outcome

Shipped the whole server-side "Remove local copy" flow behind two new `IMediathecaApi`
members, `planLocalCopyRemoval` and `removeLocalCopy`, exactly as scoped — no UI (that's
integration-mqsd3).

- **`src/Server/LocalCopyRemoval.fs`** (new) — every pure seam from the task's **What**:
  `mapJellyfinPath`, `isAncestorOf`, `deletionScope`, `matchTorrents`, `classifySeedRisk`
  (named constants `minRatio = 1.0`, `minSeedingDays = 14.0`), plus the two orchestrators
  `planLocalCopyRemoval` (`PlanEffects` record) and `execute` (`ExecuteEffects` record).
  `execute` is a fixed-order, abort-on-first-failure railway
  (PreserveWatchHistory → ResolvePath → MatchTorrents → DeleteTorrents →
  DeleteJellyfinItem → Verify → ClearLinks) implemented as nested `match` (no early-return
  primitive in F#, so each step's success case nests the remaining steps). `RemovalPlan`,
  `RemovalOutcome`, `LocalCopyTarget`, `MatchKind`, `SeedRisk`, `PlannedTorrent`,
  `RemovalCase`, `RemovalStep` live in `src/Shared/Shared.fs` (wire-crossing types on the
  `IMediathecaApi` contract) — `PlannedTorrent.SeedingTimeDays: float` stands in for
  `Qbittorrent.TorrentInfo.SeedingTime: System.TimeSpan`, which stays server-only.
- **`src/Server/Jellyfin.fs`** — `JellyfinBaseItem.Path: string option` (additive);
  `fetchJsonWithAuth` generalized into `sendWithAuth (httpMethod) …` with the GET wrapper
  kept byte-for-byte (ADR-0011's pinned `JellyfinReauthTests.fs` untouched, still 6 tests,
  still green); new `getItemWithReauth` (`GET /Items/{id}`, 404 → `Ok None`) and
  `deleteItemWithReauth` (`DELETE /Items/{id}`, 404 → `Ok ()`), both built on
  `withReauthRetry`. No `NotFound` case added to `FetchError` — a 404 folds into
  `OtherFailure "HTTP 404"` and is unfolded back into a success one layer up in each of the
  two new functions.
- **`src/Server/JellyfinStore.fs`** — `getSeriesJellyfinId` (the missing getter),
  `clearMovieJellyfinId`, `clearSeriesJellyfinId` (cascades to `jellyfin_episode`),
  `clearEpisodeJellyfinId`. All `DELETE … WHERE`, idempotent. `clearAll` untouched and
  never called by the removal flow.
- **`src/Server/JellyfinImport.fs`** — `syncMovieWatchHistory` (+ `MovieWatchSyncResult`),
  the movie sibling of `syncSeriesWatchHistory`, extracted from `Api.runJellyfinImport`'s
  previously-inline Phase 2 movie loop with identical semantics (existsOnDate /
  getRuntime / writeSession injected).
- **`src/Server/JellyfinSync.fs`** — `isSyncInProgress ()`, a thin same-lock reader beside
  `getSyncStatus`, wired as `execute`'s `IsSyncInProgress` guard.
- **`src/Server/Api.fs`** — `runJellyfinImport`'s movie Phase 2 now delegates to
  `JellyfinImport.syncMovieWatchHistory` via a factored `movieWatchHistoryEffects` helper
  (shared with `removeLocalCopy`'s preserve-watch-history step); the series `writeEpisode`
  closure is likewise factored into `seriesWatchHistoryWriteEpisode`. `planLocalCopyRemoval`
  and `removeLocalCopy` wire real SQLite/HTTP effects (`JellyfinStore`, `Qbittorrent.withSession`
  — a fresh login per operation, ADR-0071 point 7 — `Jellyfin.getItemWithReauth`/
  `deleteItemWithReauth`) into `LocalCopyRemoval.planLocalCopyRemoval`/`execute`.
- **`src/Server/Composition.fs`** — `getQbittorrentConfig` (mirrors `getJellyfinConfig`'s
  shape) and `mountRoots` (env vars `JELLYFIN_MEDIA_ROOT`/`QBITTORRENT_DOWNLOAD_ROOT`,
  defaulted, read once, never seeded into `SettingsStore`), threaded into `Api.create`'s two
  new parameters.
- **Tests** (60 new `testCase`s, all green): `LocalCopyRemovalTests.fs` (41 — every pure
  function plus `plan`/`execute` fixture-lambda coverage matching every listed acceptance
  scenario), `JellyfinItemTests.fs` (6, new file — `getItemWithReauth`/`deleteItemWithReauth`
  against a fake `HttpMessageHandler`, 200/404/401-then-retry), `JellyfinStoreTests.fs`
  (8, new file, in-memory SQLite), 5 new `syncMovieWatchHistory` cases appended to
  `JellyfinImportTests.fs`. Full suite: `npm test` → 758 passed, 0 failed. `npm run build`
  (Fable/Vite client typecheck, exercising the new Shared.fs types) green.
  Eight existing `Api.create` call sites across other test files were updated for the two
  new parameters (mechanical — a fixture qBittorrent config + `LocalCopyRemoval.defaultMountRoots`).

The three [human-eye]/builder-run acceptance criteria (live-on-harbour `curl` checks, the
`/mnt/media/files` ssh check, the live play-state-after-removal check) are explicitly left
unticked above — they are the builder's post-deploy responsibility per this task's own
Rule 7, not a worker's.

No new ADR: ADR-0071 (already written by the prior modeling pass) covers every design
decision this task implements; nothing here needed a fresh "why this, not the obvious
alternative" beyond what it already records.
