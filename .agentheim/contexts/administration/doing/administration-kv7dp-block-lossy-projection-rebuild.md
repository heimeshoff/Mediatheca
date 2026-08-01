---
id: administration-kv7dp
title: Block projection rebuild for handlers with out-of-band writers — rebuilding SeriesProjection today permanently destroys 780 refreshes' worth of TMDB metadata plus 23 Jellyfin-materialized episodes
status: doing
type: bug
context: administration
created: 2026-08-01
completed:
depends_on: []
blocks: []
tags: [projection, rebuild, data-loss, drift, series]
related_adrs: [0024, 0025, 0031, 0034, 0012]
related_research: []
prior_art: [administration-qjcp4, administration-btvqa]
---

## Why

`Projection.rebuildProjection` (`src/Server/Projection.fs:78-81`) does `handler.Drop; handler.Init;
replay`. `SeriesRefresh.applyToProjection` (`src/Server/SeriesRefresh.fs:169`) writes TMDB fetch
results directly into `series_list` / `series_detail` / `series_seasons` / `series_episodes`, and
`Series_refreshed` carries only `{refreshedAt, newEpisodeCount, previousStatus, newStatus}` —
`SeriesProjection.handleEvent`'s arm for it is an explicit no-op (`src/Server/SeriesProjection.fs:687-693`).

So a rebuild of SeriesProjection **today** permanently destroys:

- 780 refreshes' worth of episode/season metadata across 38 series,
- 161 `series_episodes` rows and 21 `series_seasons` rows that exist only in the live tables,
- 23 Jellyfin-materialized episodes and 5 Jellyfin seasons (ADR-0012),

recoverable only by re-running every TMDB refresh and the Jellyfin sync. The button is live in
Settings > Projections right now. This hazard is entirely separable from the larger refactor and
must not wait for it.

## What

- Add `Administration.lossyRebuildProjections : (string * string) list` next to `projectionTables`
  (`src/Server/Administration.fs:367`). One entry — `"SeriesProjection"` — whose reason string names
  what is destroyed **and** the recovery path (run the Series TMDB refresh job from the Jobs section;
  reload the app to trigger a Jellyfin sync).
- Add pure `Administration.lossyRebuildRejectionMessage : string -> string option`, with a
  `MEDIATHECA_ALLOW_LOSSY_REBUILD=1` env override — server-side, no UI, greppable, self-evidently
  temporary. Same "operator-facing rejection reason" shape as `driftCheckRejectionMessage`.
- Wire as a third rejection arm in `projectionRebuildStreamHandler` (~`Administration.fs:928`),
  **ordered before** the wipe-import and single-flight checks, since it claims nothing.
- Apply the same check at the second, easily-missed call site `src/Server/CinemarcoImport.fs:866`,
  falling back to `Projection.runProjection` (incremental catch-up) and `eprintfn`-ing the reason.
- Do **not** put the guard inside `Projection.rebuildProjection` — `Projection.fs` must stay free of
  admin-console knowledge, and the Expecto call sites must keep rebuilding freely.
- ADR-0034's `VACUUM INTO` backup guardrail is deliberately **not** reused: it protects the *event
  store*, but the loss here is in *projection tables*, and hand-restoring a db file is not a recovery
  path a single-user app uses. Record that reasoning in the ADR.

## Acceptance criteria

- [ ] Expecto: `lossyRebuildRejectionMessage "SeriesProjection"` returns `Some`; `"MovieProjection"` returns `None`; with `MEDIATHECA_ALLOW_LOSSY_REBUILD=1` set, `"SeriesProjection"` returns `None`.
- [ ] Expecto: a rebuild request for SeriesProjection emits a `rejected` SSE frame and leaves the `series_episodes` row count unchanged.
- [ ] Expecto: "Rebuild all" skips SeriesProjection and completes the other five handlers.
- [ ] The existing non-fatal `Rebuild_rejected` handler at `src/Client/Pages/AdminProjections/State.fs:326` is unmodified in the diff — no client change is required.
- [ ] `git diff --stat src/Client/` shows zero changed files.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

**ADR required:** *"Rebuild is blocked outright for projections with out-of-band writers"*, `scope: administration`.

The ADR must state the **retirement criterion in the Decision section, not in Consequences** — this
mechanism is built to be deleted:

> Remove a projection's entry when `checkProjectionDrift` reports 0 discrepancies for it **and** no
> module outside its own `*Projection.fs` writes any table classified `Projected` for it. When the
> list empties, delete the mechanism entirely and mark this ADR superseded.

`series-d5tpn` is the task that executes that retirement.

The ADR must also record why ADR-0034's confirm-modal guardrail was not reused: it is a frontend
task, would inherit the design-system gate, and would be permanent UI built to serve a condition we
are committing to delete.
