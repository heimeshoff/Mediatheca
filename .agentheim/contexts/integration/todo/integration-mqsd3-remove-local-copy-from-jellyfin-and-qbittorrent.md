---
id: integration-mqsd3
title: "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API
status: todo
type: feature
context: integration
created: 2026-09-10
completed:
depends_on: [design-system-001, integration-r4vzm]
blocks: []
tags: [jellyfin, qbittorrent, movies, series, storage, ui]
related_adrs: [0016, 0064, 0071]
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

The server-side flow (plan-then-execute, verified, projection-only — ADR-0071) shipped in
integration-r4vzm: `planLocalCopyRemoval: LocalCopyTarget -> Async<Result<RemovalPlan,
string>>` and `removeLocalCopy: LocalCopyTarget * string list -> Async<RemovalOutcome>` on
`IMediathecaApi`, with every wire type in `src/Shared/Shared.fs`. This task is the button
and the dialog on top of it.

## What

### The action

A **Remove local copy** button (`Icons.trash`, the muted pill style of "Search on
IPTorrents") in the action-buttons row of the movie detail page — beside "Play in Jellyfin"
in `MovieDetail/Views.fs` — and of the series detail page (`SeriesDetail/Views.fs`, the
same row that holds "Play Trailer" / "Search on IPTorrents" / In Focus). Shown only when
the detail DTO's `JellyfinId` is `Some`. It is **not** the existing "Remove movie" /
"Remove series" action (`ConfirmingRemove`, `Remove_movie`, `Confirm_remove_series`) —
that one removes the library entry; this one removes the files. Keep both, name the new
messages distinctly.

**Series DTO gap (found on the second pass):** `Shared.SeriesDetail` carries no
`JellyfinId` today, and the series page never loads the Jellyfin server URL. Add
`JellyfinId: string option` to `SeriesDetail` and populate it in
`SeriesProjection.getBySlug` with `JellyfinStore.getSeriesJellyfinId conn slug` (shipped
by integration-r4vzm) — exactly how `MovieProjection` fills the movie's `JellyfinId` from
`JellyfinStore.getMovieJellyfinId`. This is the task's only server-side change. A series
"Play in Jellyfin" link is *not* in scope (follow-up).

### The dialog — one shared component

`src/Client/Components/LocalCopyRemovalDialog.fs` (slotted after `ModalPanel.fs` in
`Client.fsproj`), with its own `Model` / `Msg` / `init` / `update` / `view`, composed into
both pages the usual way: each page's `Model` gets `LocalCopyRemoval:
LocalCopyRemovalDialog.Model option`, each page's `Msg` gets a wrapper case, and the page's
`update` maps the child `Cmd`. Rendered through `ModalPanel.viewWithFooter` — which already
applies `DesignSystem.modalPanel`, i.e. paper overlay (ADR-0016).

The dialog's `update` takes an **effects record** instead of the whole api —
`{ Plan: LocalCopyTarget -> Async<Result<RemovalPlan, string>>; Remove: LocalCopyTarget *
string list -> Async<RemovalOutcome> }` — which the pages build from `api`. Client tests
pass plain lambdas (the existing tests use `Unchecked.defaultof<IMediathecaApi>`, which
is `null` in Fable and would throw the moment `update` touched a member).

The model is a phase machine, so every decision is visible on the model and testable
without running a `Cmd` (no existing client test inspects one — ADR-0064 tests drive
`update` and assert on the model):

- `Planning` — opened; `Plan target` in flight.
- `PlanFailed of string` — the `Error` branch of `planLocalCopyRemoval`; message + Close.
- `Confirming of RemovalPlan * ticked: Set<string>` — the confirmation view (below).
- `Removing of acknowledged: string list` — `Remove (target, acknowledged)` in flight.
- `Finished of RemovalOutcome` — the outcome view (below).

Two pure functions on the model, both used by `update`/`view` and both unit-tested:

- `acknowledgedHashes : RemovalPlan -> Set<string> -> string list` — the ticked rows'
  hashes in plan order. `Confirm` moves `Confirming (plan, ticked)` to
  `Removing (acknowledgedHashes plan ticked)` and issues `Remove`.
- `canRemove : RemovalPlan -> Set<string> -> bool` — `TorrentsPresent` and every
  `plan.Torrents` hash is in `ticked`; `FilesOnly` → `true`; `AlreadyGoneInJellyfin` /
  `PathOutsideMountMap` → `false`.

### The confirmation view (`Confirming`)

- the resolved path (`font-mono`) — `plan.Path` (empty for `AlreadyGoneInJellyfin`;
  show the case banner alone then);
- a **case banner**: `TorrentsPresent` ("N torrents still seed these files") /
  `FilesOnly` ("no torrent seeds these files any more — only the Jellyfin item and its
  files will be deleted") / `AlreadyGoneInJellyfin` and `PathOutsideMountMap` (dead ends:
  no Remove button, a one-line explanation — the latter names the path and says the mount
  map does not cover it);
- one row per `plan.Torrents` entry: `Name`, **`Ratio`** and **`SeedingTimeDays`**
  (`font-mono`), a hit-and-run flag with the `HitAndRun reason` text, and a distinct,
  more prominent **pack warning** ("also contains N other files") for `PackAncestor n`;
- one checkbox per row, initialised from `PreTicked` (flagged and pack rows start
  un-ticked). **Remove** (`button.error`) enables only when `canRemove` — the tick is an
  acknowledgement, not a selection: the server deletes the files under every matched
  torrent regardless and fails at `MatchTorrents` on a partial set.

### The outcome view (`Finished`)

`RemovalOutcome.Completed` rendered in order as a checked step list with human labels
(`PreserveWatchHistory` → "Play state saved", `ResolvePath` → "Path resolved",
`MatchTorrents` → "Torrents matched", `DeleteTorrents` → "Torrents deleted (with
files)", `DeleteJellyfinItem` → "Jellyfin item deleted", `Verify` → "Verified gone",
`ClearLinks` → "Jellyfin link cleared"). When `Failure = Some (step, reason)`: the
failing step marked, the reason below it verbatim (server messages already read as
instructions — "refused: a Jellyfin sync is in progress -- try again once it finishes",
"the torrent list changed since the dialog was shown — reopen it"), and a **Try again**
button whose `Retry` message returns the model to `Planning` and re-issues `Plan` from
scratch. When `Failure = None`: a Done button that closes the dialog **and** reloads the
page's detail DTO (movie: dispatch the existing `Load_movie slug`; series: the existing
`getSeriesDetail` → `Detail_loaded` reload). `MovieProjection` / `SeriesProjection` read
the Jellyfin id from `JellyfinStore` at build time, so after `ClearLinks` the reloaded
DTO's `JellyfinId` is `None` and "Play in Jellyfin" and "Remove local copy" vanish
together — no page reload, no Jellyfin sync.

Policy is warn, never refuse (integration-r4vzm Notes). Targets: `MovieTarget slug` and
`SeriesTarget slug` (whole series). Single-episode and season entry points are follow-ups.

## Acceptance criteria

- [ ] `Shared.SeriesDetail` has `JellyfinId: string option`, populated by
      `SeriesProjection.getBySlug` via `JellyfinStore.getSeriesJellyfinId`; a server test
      on in-memory SQLite shows a series with a `jellyfin_series` row gets `Some id` and
      one without gets `None`.
- [ ] A movie whose `MovieDetail.JellyfinId` is `Some` shows "Remove local copy" in its
      action row; one with `None` does not. Same for a series via `SeriesDetail.JellyfinId`.
      (Client unit test on the pure show-predicate each page's view uses, ADR-0064.)
- [ ] `LocalCopyRemovalDialog.update` is unit-tested (ADR-0064, plain-lambda effects):
      `Plan_result (Ok plan)` moves `Planning` to `Confirming (plan, ticked)` with `ticked`
      = exactly the `PreTicked` hashes; `Toggle hash` flips one row; `Plan_result (Error e)`
      moves to `PlanFailed e`.
- [ ] `canRemove` is unit-tested: `false` while any `TorrentsPresent` row is un-ticked,
      `true` once all are ticked, `true` for `FilesOnly` with no rows, `false` for
      `AlreadyGoneInJellyfin` and `PathOutsideMountMap`.
- [ ] `Confirm` on `Confirming (plan, ticked)` moves to `Removing acknowledged` where
      `acknowledged` = exactly the ticked rows' hashes (client unit test on
      `acknowledgedHashes` and on the phase transition) — this list is what is passed to
      `Remove`.
- [ ] `Removal_result outcome` moves `Removing _` to `Finished outcome`; `Retry` on a
      `Finished` outcome with `Failure = Some _` moves back to `Planning` (client unit test
      on the phase transitions).
- [ ] The outcome view renders every `Completed` step with its label in order and, on a
      `Failure`, the failing step's label plus the reason text verbatim (client unit test
      on the pure label/row-building function the view uses).
- [ ] Every surface is paper overlay per ADR-0016 — the dialog renders through
      `ModalPanel`, no translucency, no `backdrop-filter` (`design-check` passes on the
      new view code).
- [ ] `npm run build`, `npm test` and `npm run test:client` pass; the new test file is a
      `<Compile>` item in `Client.fsproj`'s test block and ends with `Mocha.runTests`.
- [ ] Live on harbour after deploy (builder-run — a worker or verifier never touches
      harbour): after a successful removal, the movie page shows neither "Play in
      Jellyfin" nor "Remove local copy" without a browser reload or a Jellyfin sync; the
      series page likewise loses "Remove local copy".
- [ ] The dialog's copy makes the hit-and-run risk legible without reading like a warning
      wall. [human-eye]
- [ ] The pack warning is visibly more prominent than the ratio flag — it is the row that
      deletes files the user did not ask to remove. [human-eye]
- [ ] The step-by-step outcome reads as progress, not as a log dump. [human-eye]

## Notes

**Refinement (2026-09-10):** the original capture was split three ways — integration-qb7tk
(qBittorrent adapter + Settings card), integration-r4vzm (server-side plan/execute flow, all
the failure semantics and tests), and this task (UI). All decisions — projection-only
(ADR-0071), warn-never-refuse, everything-listed-everything-acknowledged, pack-torrent
handling, env-var mount roots, SettingsStore credentials — are recorded in r4vzm's Notes.

**Second refinement pass (2026-09-10, code-grounded after integration-r4vzm shipped; edit
to override):**

1. **The series page has no Jellyfin id to key on.** `Shared.SeriesDetail` has no
   `JellyfinId` field and `SeriesDetail/Types.fs` holds no `JellyfinServerUrl`; only the
   movie DTO (`MovieDetail.JellyfinId`, filled from `JellyfinStore.getMovieJellyfinId` in
   `MovieProjection`) has one. The one-line server addition above is the fix; the getter
   it needs (`getSeriesJellyfinId`) exists since r4vzm. So this task is *almost* pure
   client work, not entirely — the first pass's "pure client work" was optimistic.
2. **No client test inspects a `Cmd`.** Every `*.test.fs` drives `update` with
   `Unchecked.defaultof<IMediathecaApi>` and asserts on the model. Hence the phase-machine
   model (the acknowledged list lives in `Removing acknowledged`), the two pure functions,
   and the effects record in place of the api (a `null` api throws on first member access
   in Fable; `Cmd.OfAsync.perform api.x …` touches the member at `update` time).
3. **Wire shapes as shipped:** `removeLocalCopy` takes a **tuple**
   (`LocalCopyTarget * string list`); the torrent row field is `SeedingTimeDays: float`
   (not `SeedingTime`); `AlreadyGoneInJellyfin` comes back with `Path = ""`;
   `PathOutsideMountMap` with the unmapped path; the sync-in-progress refusal is an
   outcome with `Failure = Some (PreserveWatchHistory, "refused: a Jellyfin sync is in
   progress -- …")` and `Completed = []`, not an exception — the outcome view covers it.
4. **The existing "Remove movie" confirm** (`ConfirmingRemove`, an inline error-tinted
   panel driven from the ActionMenu) is a different action and stays as is; the new dialog
   is a modal, shared by both pages, because the confirmation + outcome UI is identical
   for movie and series targets.
5. **Reload, don't patch:** after success the page re-fetches its detail DTO through the
   message it already has, rather than the client blanking `JellyfinId` locally — the
   projection is the source of truth for "is it on the server".
6. The live-on-harbour criterion is builder-run after deploy, as in r4vzm.

**Escape hatch:** if the series entry point runs long, ship movie-only here and capture the
series entry point (DTO field + button) as its own task; the server supports both targets.

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

**Follow-ups (capture separately when wanted):** a "Play in Jellyfin" link on the series
page (the DTO field this task adds makes it a few lines); season-level removal (needs a
season id store); single-episode entry point; a hard-refuse hit-and-run mode; the harbour
fleet docs note that mediatheca holds qBittorrent credentials (landed with
integration-qb7tk).
