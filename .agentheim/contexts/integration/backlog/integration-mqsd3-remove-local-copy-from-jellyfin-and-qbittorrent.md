---
id: integration-mqsd3
title: "Remove local copy" button — delete the torrent and its files from qBittorrent and the item from Jellyfin in one deterministic, verified flow, then clear the Jellyfin link in Mediatheca
status: backlog
type: feature
context: integration
created: 2026-09-10
completed:
depends_on: [design-system-001]
blocks: []
tags: [jellyfin, qbittorrent, sync, movies, series, settings, storage]
related_adrs: [0011]
related_research: []
prior_art: [integration-002]
---

## Why

Mediatheca already knows which movies and series exist on the home server (the Jellyfin id
is stored per movie, series and episode, and the detail page shows a Play button when it is
set). Removing a local copy is today a three-place manual chore: stop and delete the torrent
in the qBittorrent UI, wait for or trigger a Jellyfin library scan, and hope the Jellyfin
item actually disappears. The library app that shows "this is on the server" should also be
the place that says "and now it is not" — and it should be *certain* the files are gone from
the disk and the item is gone from Jellyfin's database, not "probably gone after the next
scan".

## What

A **Remove local copy** action on the movie detail page (and on series / season / episode
where a Jellyfin id exists) that runs this sequence server-side and reports each step:

1. **Preserve watch history first.** Import this item's Jellyfin play state into Mediatheca
   before anything is deleted — Jellyfin drops the item's user data together with the item.
2. **Resolve the item's path** from Jellyfin (`GET /Items/{id}` → `Path`).
3. **Match torrents in qBittorrent** whose `content_path` / `save_path` lies under that
   path after swapping the mount prefix (Jellyfin sees `/media/...`, qBittorrent sees
   `/downloads/...`; both are `/mnt/media/files` on the host). A movie matches one torrent,
   a series matches every torrent under its show folder, an episode matches one file.
4. **Delete the torrents with their files** — `POST /api/v2/torrents/delete` with
   `deleteFiles=true`.
5. **Delete the Jellyfin item** — `DELETE /Items/{id}`. Jellyfin removes what the torrent
   delete left behind (a movie's own folder, artwork sidecars it wrote next to a bare file)
   and drops the database row. It tolerates files that are already gone.
6. **Verify** — `GET /Items/{id}` returns 404 and qBittorrent no longer lists the hashes.
   Only then clear the Jellyfin id(s) in Mediatheca and record that the local copy was
   removed. Any failure before the verify step stops the flow and is shown to the user
   with the step it failed at; nothing is silently half-done.

A confirmation dialog precedes the action and shows: the resolved path, the matched torrents
with their **ratio and seeding time** (private-tracker hit-and-run risk), and which case the
item is in (torrent still present vs. torrent already removed, files only).

## Acceptance criteria

- [ ] A movie with a Jellyfin id shows a "Remove local copy" action; after confirming, the
      Jellyfin item returns 404, its files are gone from `/mnt/media/files`, the torrent is
      gone from qBittorrent, and the movie's Play button is gone without waiting for the next
      Jellyfin sync.
- [ ] Removing a series removes every torrent under its show folder, the show folder itself,
      and the Jellyfin series item; the series and all its episodes lose their Jellyfin ids.
- [ ] An item whose torrent was already removed from qBittorrent (files only, no torrent)
      is handled: the dialog says so, and the Jellyfin delete alone removes the files and
      the database row.
- [ ] The item's Jellyfin play state is imported into Mediatheca before deletion and is still
      visible afterwards (watched status / last played).
- [ ] The confirmation dialog shows each matched torrent's ratio and seeding time before the
      user confirms.
- [ ] A failure at any step (qBittorrent unreachable, Jellyfin 401 after one re-auth, delete
      rejected) aborts the remaining steps and surfaces the failing step; the Jellyfin id is
      **not** cleared unless the verify step passed.
- [ ] qBittorrent credentials (URL, username, password) are stored and tested from Settings
      the same way the Jellyfin credentials are; the qBittorrent Web API is used with its
      normal auth (no "bypass auth for localhost" — behind the Tailscale sidecar every client
      looks like localhost).
- [ ] The Jellyfin calls reuse `Jellyfin.withReauthRetry` (ADR-0011) — no second auth path.
- [ ] The dialog's copy makes the hit-and-run risk legible without reading like a warning
      wall. [human-eye]

## Notes

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
  sidecar (`https://jellyfin.elver-minor.ts.net/` today; `https://qb.elver-minor.ts.net/`
  would be the same mechanism).

**Open questions for refinement:**

- Hit-and-run policy: warn only, or refuse to delete a torrent below the tracker's minimum
  ratio / seed time? (Builder's call — the tracker is IPTorrents.)
- Season granularity: a season maps cleanly only if its episodes live in a season subfolder;
  otherwise offer whole-series and single-episode only.
- Modelling: is "local copy removed" a domain event on the Movies / Series aggregate (fits
  the event-sourced style, and the Journal might want to know) or projection-only like the
  Jellyfin id itself? The nightly Jellyfin sync's `JellyfinStore.clearAll` + repopulate would
  drop the id eventually anyway; the button must not rely on that.
- Cross-seed guard: if more than one torrent matches the same files, list them all and require
  an explicit tick per torrent rather than deleting silently.
- Where do qBittorrent credentials live — SettingsStore like Jellyfin (preferred, mirrors the
  existing pattern), or the stack `.env` on harbour?

Related: the harbour repo's fleet docs should get a one-line note once this ships, since
mediatheca would then hold qBittorrent credentials.
