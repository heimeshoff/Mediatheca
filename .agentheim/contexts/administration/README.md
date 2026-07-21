# Administration

## Purpose
**Operational plumbing.** Settings, event store, projection mechanics, event browser, image storage. The infrastructure surface that keeps the single-user app running and inspectable.

## Classification
**generic** — Generic infra for any event-sourced app of this shape.

## Actors
Single user, in an operator role (rebuilding projections, inspecting the event log, configuring API keys).

## Ubiquitous language

- **Event** — an immutable record appended to the event store. Each carries an aggregate id, a type, payload, and a position.
- **Event store** — the SQLite-backed append-only log (`EventStore.fs`). WAL mode, NORMAL sync, FK enabled.
- **Projection** — derived read model rebuilt from the event log (`Projection.fs` plus per-BC `*Projection.fs`).
- **Event browser** — the UI surface for inspecting / searching the event log (task 014). Lives as the Events tab of the Admin console. Search is full-text over event payloads via a SQLite FTS5 index (`events_fts`, external-content over `events.data`); filters (stream, event type, bounded context, timestamp range) compose and are expressed as `EventFilter`; results page via `EventPageQuery`/`EventPage` using keyset (not offset) pagination on `global_position` — see ADR-0020. Stream ids in the explorer's rows are clickable and open the stream drill-in. A **Follow toggle** puts the browser into live-tail mode: polls `IAdminApi.getEventsAfter` every ~2s for events after the last-seen `global_position`, matching the active filters, and prepends them with a brief arrival highlight. Follow is only available on the newest page (`CurrentBefore = None`) and is force-stopped by pagination in either direction, by explicit toggle-off, and by navigating away from the Admin page entirely (root `State.Url_changed` bumps the Follow epoch on `AdminModel` when leaving `Admin _` for any other page, so a poll or in-flight response scheduled before the navigation can't outlive it) — see ADR-0023.
- **Stream drill-in** — `/admin/streams/<streamId>` (`Router.Stream_detail`, `src/Client/Pages/StreamDetail/`): one stream's full event history (via `EventStore.readStream` + `EventFormatting.formatEvent`) juxtaposed with what the matching projection currently says about it, per ADR-0002. Each timeline entry has a raw-JSON toggle (data, metadata, global/stream position); events with no known formatter render as raw JSON marked "unformatted" instead of disappearing (feeds the future drift report, `administration-btvqa`). A "current state" panel shows the projection row(s) for Movie/Series/Game/Friend/Catalog streams, dispatched by stream prefix onto each BC's existing `*Projection.getBySlug`, with a link to the media detail page when one exists. Known reference fields in payloads (`friendSlug`, `movieSlug`, `seriesSlug`, `gameSlug`) render as cross-links to the referenced stream's own drill-in page — see ADR-0022 for why a dangling reference is safe to link (it degrades to an empty timeline, not an error).
- **Admin console** — the `/admin` tabbed section (`src/Client/Pages/Admin/`) hosting all operator-facing tooling: Events (event browser), Projections, Health, Jobs, Surgery. Tabs are URL-addressable (`/admin/events`, `/admin/projections`, ...) via `Router.AdminTab`. `/events` is a legacy alias that resolves to the Events tab. `/admin/streams/<streamId>` (the stream drill-in) is a sibling top-level route, not an `AdminTab` variant, since it's parameterized.
- **`IAdminApi`** — the admin console's own Fable.Remoting contract (`src/Shared/Shared.fs`), separate from `IMediathecaApi` so admin plumbing doesn't bloat the domain API surface (ADR-0004 allows multiple Remoting APIs on one server; see ADR-0017). Routed under `/api/admin/{Method}` via `AdminRoute.builder`. Implemented server-side by `src/Server/Administration.fs` (`Administration.create conn dbPath imagesDir projectionHandlers` — `dbPath`/`imagesDir` are the same paths `Composition.fs` derives from `DATA_DIR`; `projectionHandlers` is the same registry `Api.create` uses). Event-explorer methods: `getEventPage` (filtered, keyset-paginated), `getEventsAfter` (filtered live-tail poll, ADR-0023), `getEventStreams`, `getEventTypes`, `getBoundedContexts`. Stream drill-in: `getStreamDetail: string -> Async<StreamDetailDto>` (`StreamDetailDto` = `Entries: StreamTimelineEntry list` + `ProjectionRows: ProjectionStateRow list`). Health method: `getHealthStats`. Projections method: `getProjectionStats` (see the Projections tab bullet below).
- **Projections tab** (`/admin/projections`, `src/Client/Pages/AdminProjections/`) — checkpoint/lag/row-count listing for every registered `Projection.ProjectionHandler`, plus an explicit "Rebuild" command per projection (and a "Rebuild all" that drives the same command sequentially across all handlers). The listing comes from `IAdminApi.getProjectionStats : unit -> Async<ProjectionStatRow list>`: for each handler, `Projection.getCheckpointInfo` (checkpoint position + `updated_at`), `Lag` (store head via `EventStore.getMaxGlobalPosition` minus checkpoint), and per-table `RowCount`s from a small hardcoded `projectionName -> table names` map in `Administration.fs` (same "admin-console-only knowledge of a BC's naming convention" pattern as `boundedContextPrefixes`). Rebuild is **not** a Remoting call — it's a raw Giraffe SSE route, `Administration.projectionRebuildStreamHandler`, mounted at `/api/stream/rebuild-projection/{name}` (same streaming pattern as `Api.steamFamilyImportHandler`: `text/event-stream`, `data: {"type":...}\n\n` framed messages). It calls `Projection.rebuildProjectionWithProgress` (drop + replay via the existing `Projection.rebuildProjection` machinery, but reporting a `Projection.RebuildProgress` — `Position`/`Head`/`EventsProcessed` — after every 100-event batch) and streams `progress` events, then a `complete` event. A module-level `ConcurrentDictionary` (`Administration.rebuildingProjections`) guards against two concurrent rebuilds of the same projection: a second request for a projection already rebuilding gets a `rejected` SSE event instead of running. The startup-time forced rebuild of Series/Game that used to live in `Composition.fs` is retired — `buildApp` now only calls `Projection.startAllProjections` (plain incremental catch-up); a full rebuild is this explicit operator command instead. See ADR-0024.
- **`EventFilter`** / **`EventPageQuery`** / **`EventPage`** — the composable filter and keyset-pagination query/result shapes for the event explorer (`src/Shared/Shared.fs`; server-side query engine in `EventStore.queryEventPage`, `EventStore.QueryFilter`). See ADR-0020. Reused as-is by the live-tail query.
- **`EventTailQuery`** — "everything after global position `After` matching `Filter`, capped at `Limit`" (`src/Shared/Shared.fs`; server-side `EventStore.queryEventsAfter`, sharing `EventStore.buildFilterConditions` with `queryEventPage`). The ascending counterpart ADR-0020 deliberately left off `EventPageQuery`. See ADR-0023.
- **Health tab** (`/admin/health`, `src/Client/Pages/AdminHealth/`) — store-wide diagnostics: total event count, per-bounded-context breakdown, a 90-day daily-activity sparkline, top-10 largest streams, distinct/top event types, and storage sizes (`mediatheca.db`, WAL sidecar, `images/` cache). Loads from one aggregate DTO, `HealthStats` (`IAdminApi.getHealthStats`), so the whole tab is a single round trip. The per-stream and per-event-type aggregates are index-only `GROUP BY` scans (`EventStore.getEventCountsByStream`/`getEventCountsByType`); the daily counts are bounded to the ~90-day window via an indexed timestamp range (`EventStore.getDailyEventCounts`) rather than scanning the whole table. See ADR-0021 for the full cost reasoning and the growth threshold at which this would need a materialized summary table instead.
- **Setting** — a configuration value persisted in `SettingsStore.fs`. API keys, sync cadence, Jellyfin URL, Steam credentials, etc.
- **Image ref** — a stable identifier for a stored image (posters, backdrops, friend avatars). The image store (`ImageStore.fs`) is the source of truth; aggregates reference images by ref.
- **Slug** — domain-stable identifier used across BCs (friend slug, movie slug, etc.). Generated by `Slug` module in Shared.

## Aggregates

No domain aggregates. The store and registry tables are infrastructure.

## Key events

The event store **carries** events from every BC but does not own a stream of its own. There is no `Administration_*` event family.

## Key commands

Operational: "rebuild projection X", "set setting Y", "save image Z". These are command-shaped but not in the same sense as core BC commands.

## Relationships with other contexts

- **Shared kernel** with every BC: the event store and image store are consumed everywhere.
- **Supports:** Integration (settings live here, adapters read them).

## Frontend gate

Frontend tasks in this BC (event browser, settings UI) **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- Backup / restore strategy for the event store (currently undocumented).
