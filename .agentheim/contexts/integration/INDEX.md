# integration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 2
- **Todo:** 0
- **Doing:** 0
- **Done:** 8
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **integration-ygwsa** — Spike — mint Steam Family access tokens from a stored refresh token (SteamKit2) (spike) — `done/integration-ygwsa-steam-family-token-spike.md`
- **integration-m4k7p** -- Materialize a missing season/episode from Jellyfin when TMDB lacks it -- `feature` -- `done/integration-m4k7p-materialize-missing-season-from-jellyfin.md`
- **integration-006** -- Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up -- `bug` -- `done/integration-006-nightly-refresh-skips-ended-series.md`
- **integration-005** -- Spike — fallback metadata source when TMDB lags on new seasons -- `spike` -- `done/integration-005-fallback-metadata-source-spike.md`
- **integration-004** -- Steam playtime sync silently drops same-day deltas -- `bug` -- `done/integration-004-steam-sync-drops-same-day-delta.md`
- **integration-003** -- Surface the persisted Jellyfin sync failure in the Settings UI -- `feature` -- `done/integration-003-surface-jellyfin-sync-failure-in-settings.md`
- **integration-002** -- Re-authenticate Jellyfin and retry once on a 401/403 during sync -- `bug` -- `done/integration-002-jellyfin-reauth-on-401.md`
- **integration-001** -- Jellyfin sync silently stopped writing episode watch history -- `bug` -- `done/integration-001-jellyfin-sync-silently-stopped.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **integration-007** -- Fetch Jellyfin episode stills when materializing a missing season -- `feature` -- `backlog/integration-007-fetch-jellyfin-episode-stills-on-materialize.md`
- **integration-hebjs** -- One-click Steam Family import — automatic access-token acquisition -- `feature` -- `backlog/integration-hebjs-one-click-steam-family-import.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0019** -- Steam Family token refresh — pure mint-and-retry seam shipped, live audience/scope verification deferred to integration-hebjs -- 2026-07-20 -- `knowledge/decisions/0019-steam-family-token-refresh-seam-pending-audience-verification.md`
- **0012** -- Jellyfin materializes missing seasons as a projection-only supplement, TMDB stays authoritative -- 2026-06-26 -- `knowledge/decisions/0012-jellyfin-materializes-missing-seasons-as-projection-supplement.md`
- **0011** -- Jellyfin self-heals a rejected token via a pure re-auth-and-retry orchestration -- 2026-05-27 -- `knowledge/decisions/0011-jellyfin-reauth-on-401.md`
- **0010** -- Jellyfin sync persists its last result and isolates per-item faults -- 2026-05-27 -- `knowledge/decisions/0010-jellyfin-sync-observability-fault-isolation.md`
<!-- adr-local:end -->

## Research touching this BC

<!-- research-local:start -->
- **steam-family-api-auto-token-refresh** -- SteamKit2 QR login + refresh tokens vs. browser-scraped `access_token` for `IFamilyGroupsService`; audience/scope of minted tokens unconfirmed -- 2026-07-20 -- `knowledge/research/steam-family-api-auto-token-refresh-2026-07-20.md`
- **tv-series-metadata-fallback-sources** -- TheTVDB vs Trakt vs Jellyfin-as-source as a fallback when TMDB lags on a new season; recommends Jellyfin-as-source supplementing TMDB -- 2026-06-26 -- `knowledge/research/tv-series-metadata-fallback-sources-2026-06-26.md`
<!-- research-local:end -->

## Concepts (opt-in synthesis pages)

<!-- concepts:start -->
<!-- no concept pages yet -->
<!-- concepts:end -->

## Pointers

- BC README (ubiquitous language, invariants): `README.md`
