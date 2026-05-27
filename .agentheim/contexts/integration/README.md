# Integration

## Purpose
**Adapters to external systems.** Translates external shapes (TMDB / RAWG / Steam / HLTB / Jellyfin / Cinemarco) into commands the core BCs accept, and runs scheduled sync jobs that pull external state on a cadence. The anticorruption layer that keeps core BCs free of HTTP and vendor JSON.

## Classification
**generic** — Boring plumbing where boring choices are correct.

## Actors
External services (TMDB API, RAWG API, Steam Web API, HLTB scraping, Jellyfin server, Cinemarco) and the single user (triggering manual refresh / sync actions).

## Ubiquitous language

- **Adapter** — module that owns one external system (`Tmdb.fs`, `Rawg.fs`, `Steam.fs`, `HowLongToBeat.fs`, `Jellyfin.fs`, `CinemarcoImport.fs`). Translates DTOs ↔ domain language.
- **External id** — TMDB id, RAWG id, Steam appId, Jellyfin item id. Stored on core aggregates so adapters can re-sync.
- **Import** — one-time pull of items from an external source into core BCs (e.g. Cinemarco favorites become Movies + a Catalog).
- **Sync** — repeating pull of external state. Two cadences exist: **scheduled** syncs run on a server timer (`ScheduledJobs.fs`: Steam library + play time, Series TMDB refresh), while the **Jellyfin** sync is *client-initiated* — triggered when the SPA loads, gated by a 5-minute cooldown and an in-progress lock in `JellyfinSync.fs` (not a `ScheduledJobs` job). The sync's last **result** (counts + error list / failure message) is persisted via `SettingsStore` so a breakage is visible across restarts, and any per-item failure surfaces as `JellyfinSyncStatus.SyncFailed` (integration-001). A Jellyfin fetch that returns **401/403** (a rejected token) triggers exactly one self-healing **re-auth**: the adapter re-authenticates with the stored `jellyfin_username`/`jellyfin_password`, persists the fresh `jellyfin_access_token` + `jellyfin_user_id`, and retries the failed request once; a second rejection, a failed re-auth, or missing credentials surface a clear "re-authentication" `SyncFailed` instead of looping (integration-002, `Jellyfin.withReauthRetry`).
- **Refresh** — user-triggered re-fetch of a single item's metadata (e.g. "refresh this series from TMDB").
- **Scheduled job** — recurring task scheduled by `ScheduledJobs.fs` (Steam playtime, Series episode/TMDB refresh per task 042, etc.). Note: Jellyfin auto-sync (task 037) is *not* a scheduled job — it is client-init triggered with a cooldown.

## Aggregates

No domain aggregates. The closest things to internal state are the **scheduled-job registry** and per-adapter caches; both are infrastructure, not aggregates.

## Key events

Integration **does not own its own event stream**. It emits *commands* into the core BCs (`Add_movie_to_library`, `Refresh_series_from_tmdb`, `Set_play_time`, `Set_hltb_hours`, …). The resulting events live in the core BC's stream.

## Key commands

Adapters call into the core BCs; they don't expose their own command bus to the outside. User-facing commands here are operational: "Sync now", "Refresh from TMDB", "Connect with Steam" (per task 045).

## Relationships with other contexts

- **Upstream of (via anticorruption):** Movies, Series, Games. Adapters translate external DTOs into core commands.
- **Operational dependency:** Administration (settings — API keys, sync cadence — live there; see `SettingsStore.fs`).

## Frontend gate

Frontend tasks in this BC (sync UI, "connect with Steam" flows, refresh buttons) **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- How to expose adapter failures back to the UI consistently — currently varies per adapter.
- Whether to standardize on a single "refresh queue" pattern across adapters or keep them bespoke.
