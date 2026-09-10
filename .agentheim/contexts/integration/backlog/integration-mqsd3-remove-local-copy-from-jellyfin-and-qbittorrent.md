---
id: integration-mqsd3
title: "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API
status: backlog
type: feature
context: integration
created: 2026-09-10
completed:
depends_on: [design-system-001, integration-r4vzm]
blocks: []
tags: [jellyfin, qbittorrent, movies, series, storage, ui]
related_adrs: [0016, 0064, 0070]
related_research: []
prior_art: [integration-003]
---

## Why

Mediatheca already knows which movies and series exist on the home server (the Jellyfin id
is stored per movie, series and episode, and the movie detail page shows a Play button when
it is set). Removing a local copy is today a three-place manual chore: stop and delete the
torrent in the qBittorrent UI, wait for or trigger a Jellyfin library scan, and hope the
Jellyfin item actually disappears. The library app that shows "this is on the server"
should also be the place that says "and now it is not" — and it should be *certain* the
files are gone from the disk and the item is gone from Jellyfin's database, not "probably
gone after the next scan".

The server-side flow (plan-then-execute, verified, projection-only — ADR-0070) ships in
integration-r4vzm. This task is the button and the dialog on top of it.

## What

A **Remove local copy** action on the movie detail page (beside "Play in Jellyfin",
`MovieDetail/Views.fs`) and on the series detail page, shown when the item has a Jellyfin
id. Clicking it calls `planLocalCopyRemoval` and opens a confirmation dialog
(`DesignSystem.modalPanel`, paper overlay — ADR-0016) that shows:

- the resolved path (`font-mono`);
- a case banner: torrents present / files only (torrent already gone) / already gone in
  Jellyfin / path outside the mount map (the last two are dead ends: no Remove button, a
  one-line explanation);
- one row per matched torrent: name, **ratio** and **seeding time** (`font-mono`), a
  hit-and-run flag when `Risk = HitAndRun`, and a distinct **pack warning** ("also
  contains N other files") when `Match = PackAncestor n`;
- one checkbox per torrent row, initialised from `PreTicked` (flagged and pack rows start
  un-ticked). **Remove** enables only when every row is ticked — the tick is an
  acknowledgement, not a selection (the server deletes the files under every matched
  torrent regardless, and rejects a partial set).

Confirming calls `removeLocalCopy target acknowledgedHashes`. The dialog then shows the
outcome step by step (`RemovalOutcome.Completed` in order, and the failing step + reason if
`Failure` is set, with a "Try again" that re-plans from scratch). On success the page
reloads its detail DTO so the Play button and the action itself disappear at once.

Policy is warn, never refuse (integration-r4vzm Notes). Targets: movie and whole series.
Single-episode and season entry points are follow-ups.

## Acceptance criteria

- [ ] A movie with a Jellyfin id shows "Remove local copy" on its detail page; a movie
      without one does not. Same for a series. (Client unit test on the view-model
      predicate, ADR-0064.)
- [ ] The dialog's MVU `update` is unit-tested (ADR-0064): rows initialise from
      `PreTicked`; Remove is disabled while any row is un-ticked and enabled when all are
      ticked; the `FilesOnly` case enables Remove with no rows; the `AlreadyGoneInJellyfin`
      and `PathOutsideMountMap` cases render no Remove button.
- [ ] The acknowledged hashes sent to `removeLocalCopy` are exactly the ticked rows' hashes
      (client unit test on the command payload).
- [ ] After a successful removal on harbour, the movie page shows neither "Play in
      Jellyfin" nor "Remove local copy" without a page reload or a Jellyfin sync.
- [ ] A `Failure` outcome renders the failing step's name and reason, and "Try again"
      re-runs `planLocalCopyRemoval` (client unit test on the message flow).
- [ ] Every surface is paper overlay per ADR-0016 — no translucency, no `backdrop-filter`
      (`design-check` passes on the new view code).
- [ ] `npm run build`, `npm test` and `npm run test:client` pass.
- [ ] The dialog's copy makes the hit-and-run risk legible without reading like a warning
      wall. [human-eye]
- [ ] The pack warning is visibly more prominent than the ratio flag — it is the row that
      deletes files the user did not ask to remove. [human-eye]
- [ ] The step-by-step outcome reads as progress, not as a log dump. [human-eye]

## Notes

**Refinement (2026-09-10):** the original capture was split three ways — integration-qb7tk
(qBittorrent adapter + Settings card), integration-r4vzm (server-side plan/execute flow, all
the failure semantics and tests), and this task (UI). All decisions — projection-only
(ADR-0070), warn-never-refuse, everything-listed-everything-acknowledged, pack-torrent
handling, env-var mount roots, SettingsStore credentials — are recorded in r4vzm's Notes.

**Escape hatch:** if the series entry point runs long, ship movie-only here and capture the
series entry point as its own client-only task; the server supports both targets.

**Verified on harbour 2026-09-10 (infrastructure side needs no change):**

- qBittorrent downloads straight into the Jellyfin library folders: every torrent's save path
  is `/downloads/movies` or `/downloads/shows/<show>`; Jellyfin's libraries are
  `/media/movies` and `/media/shows`. Same files, different mount prefix. No copies, no
  hardlinks.
- The Jellyfin user Mediatheca logs in as (`tatonka`) is an administrator with
  "allow media deletion" enabled — `DELETE /Items/{id}` works with the token already held.
- All library files are owned by uid 1000; Jellyfin runs as `1000:1000`, qBittorrent as
  `PUID=1000`. Neither delete hits a permission wall.
- qBittorrent's WebUI runs with default auth (localhost auth required, CSRF on). Server-to-
  server calls without an `Origin` header pass CSRF; login is `POST /api/v2/auth/login`
  (cookie `SID`), then `GET /api/v2/torrents/info` (fields `hash`, `name`, `save_path`,
  `content_path`, `ratio`, `seeding_time`) and `POST /api/v2/torrents/delete`.
- The movies folder holds ~44 movie folders but qBittorrent knows only 28 movie torrents, so
  the "torrent already gone" branch is a real, common case, not an edge case.
- Jellyfin delete semantics: a movie in its own folder → whole folder deleted; a bare file in
  a mixed folder → the file plus same-basename artwork sidecars. Item and its user data are
  removed from the database. Path already missing → still removes the row.
- Both services are reached from the mediatheca container over the tailnet through its own
  sidecar. The mediatheca container has **no** mount of `/mnt/media/files`.

**Follow-ups (capture separately when wanted):** season-level removal (needs a season id
store); single-episode entry point; a hard-refuse hit-and-run mode; the harbour fleet docs
note that mediatheca holds qBittorrent credentials (lands with integration-qb7tk).
