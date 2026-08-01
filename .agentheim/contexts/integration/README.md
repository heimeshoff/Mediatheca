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
- **Refresh token** — a long-lived credential (Steam: ~1 year; obtained via a one-time interactive login) from which an adapter mints short-lived **access tokens** on demand, so expiry never becomes user-facing manual work. The Jellyfin adapter already does this with a username/password credential (see re-auth above, ADR-0011). `Steam.withTokenRefresh` (integration-ygwsa spike, ADR-0019) ships the same pure mint-and-retry orchestration shape for the Steam Family `access_token` currently scraped by hand from Chrome DevTools — the seam is built and unit-tested, but whether a SteamKit2-minted access token actually carries the audience/scope `IFamilyGroupsService` requires is **unverified**; that empirical check is integration-hebjs's first task, see ADR-0019.
- **Refresh** — user-triggered re-fetch of a single item's metadata (e.g. "refresh this series from TMDB").
- **Materialize** — fill a metadata gap a primary source has not (yet) covered by writing rows from a *secondary* source, tagged with provenance so the primary source enriches them later. Today: when TMDB lags on a new season, the Jellyfin sync materializes the missing season/episode metadata from the user's Jellyfin server (`JellyfinImport.materializeMissingEpisodes`), tagging rows `source='jellyfin'` on `series_episodes`/`series_seasons`. TMDB stays authoritative — its `INSERT OR REPLACE` resets `source` to the `'tmdb'` default and enriches in place, clearing the per-episode "metadata pending" badge (`EpisodeDto.MetadataPending`). Projection-only, no new event (integration-m4k7p, ADR 0012). The materialization also fetches a still image best-effort (`Jellyfin.getPrimaryImageWithReauth` + `JellyfinImport.fetchEpisodeStill`, reusing the ADR-0011 re-auth policy), stored at a **distinct `stills/{slug}-sXXeYY-jellyfin.jpg` path** rather than TMDB's canonical `stills/{slug}-sXXeYY.jpg` — this keeps `SeriesRefresh`'s `ImageStore.imageExists` short-circuit from ever seeing the Jellyfin file at TMDB's path, so a later TMDB refresh still downloads and repoints `still_ref` at its own canonical file. Any still-fetch failure degrades silently to `still_ref = NULL`, never a sync error (integration-007, closes the ADR 0012 deferral). Once TMDB later enriches an episode, the `-jellyfin.jpg` file becomes unreferenced on disk — a deliberately accepted orphan (small, bounded, not proactively cleaned up to avoid coupling `SeriesRefresh` to Jellyfin storage conventions) that Administration's image-cache orphan scan/purge (ADR-0025) can find and reclaim like any other orphan (ADR-0039).
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
- Whether a SteamKit2-minted access token (refresh-token derived) actually carries the audience/scope `IFamilyGroupsService` requires — unresolved by integration-ygwsa's research, empirical answer deferred to integration-hebjs (ADR-0019).
