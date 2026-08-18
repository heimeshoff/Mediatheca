# integration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 0
- **Doing:** 0
- **Done:** 17
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
- **integration-n3vqa** — Incremental Steam Family import — answer "what's new in the family library since I last checked" and only enrich the newcomers (feature) — `done/integration-n3vqa-incremental-family-import-whats-new.md`
- **integration-k4vqm** — An empty `GetOwnedGames` response is treated as success everywhere — the key test probes a third party's private profile and calls a good key "may be invalid", while the import and the scheduled sync silently degrade (bug) — `done/integration-k4vqm-empty-owned-games-is-not-success.md`
- **integration-p2hxn** — Accept and document the MobileApp-from-datacenter-IP login signature as a known Steam account-flag risk — mitigations, a no-speculative-reconnect rule, and an escalation ladder (decision) — `done/integration-p2hxn-accept-document-steam-login-signature-risk.md`
- **integration-w7ktb** — Steam storefront calls are paced by the caller, not the Adapter — the family import paces not at all; move throttling into `Steam.fs` so every storefront caller inherits it (bug) — `done/integration-w7ktb-adapter-owned-steam-storefront-throttle.md`
- **integration-r8kwd** — Steam Family import aborts with an opaque 401 that comes from the Web-API-key `GetOwnedGames` supplement, not the family token — make the supplement non-fatal and attribute credential failures to the right credential (bug) — `done/integration-r8kwd-steam-family-import-opaque-401-from-web-api-key.md`
- **integration-w8fkr** — Retire the Cinemarco import — delete the Settings card, the `importFromCinemarco` contract member, and `CinemarcoImport.fs` (refactor) — `done/integration-w8fkr-retire-cinemarco-import.md`
- **integration-hebjs** — One-click Steam Family import — automatic access-token acquisition (feature) — `done/integration-hebjs-one-click-steam-family-import.md`
- **integration-q7wv3** — Episodes materialized before integration-007 never get a still — the backfill gap (bug) — `done/integration-q7wv3-backfill-jellyfin-stills-for-existing-materialized-episodes.md`
- **integration-007** — Fetch Jellyfin episode stills when materializing a missing season (feature) — `done/integration-007-fetch-jellyfin-episode-stills-on-materialize.md`
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
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0069** -- The Steam Family import diffs before it enriches: `GetSharedLibraryApps` is classified against `GameProjection.findBySteamAppId`, and only *new* apps get a store `appdetails` fetch — a steady-state import with zero new apps is a fixed **3** outbound Steam requests instead of one per title. *Arrivals* (new apps, plus already-known apps whose `rt_time_acquired` postdates `steam_family_last_sync`) are named with date and family member and persisted to `steam_family_last_result` so Settings survives a reload; per ADR-0043 an arrival is cache, not an event. A second explicit `FullReenrich` mode reproduces the old fetch-everything behaviour. Complements ADR-0066: that one owns request *spacing*, this one owns request *count* -- 2026-08-18 -- `knowledge/decisions/0069-incremental-family-import-diff-and-full-reenrich-wiring.md`
- **0068** -- An empty Steam owned/recently-played-games response is **inconclusive**, not success and not failure (amends ADR-0065): `{"response":{}}` means *either* "owns nothing" *or* "Game details privacy is not Public", and no caller can tell which. `testSteamApiKey` probes the builder's own stored `steam_id` (falling back to a profile-independent key-only endpoint) instead of a hardcoded third-party SteamID, and yields three distinct outcomes — rejected / valid / valid-but-inconclusive. An empty family-import supplement no longer clears `steam_api_key_last_error`, and the scheduled playtime sync persists a `KeyRejected` notice instead of no-oping silently -- 2026-08-18 -- `knowledge/decisions/0068-steam-empty-owned-games-is-inconclusive-not-failure.md`
- **0067** -- The Steam Connect QR ceremony's `MobileApp`-platform login from the Docker host's datacenter IP is an **accepted risk**, not a fixable defect: reversing ADR-0019 point 2 would force a permanent SteamKit2 + live-CM dependency into the server. Every claim about Valve's detection is labelled speculation. Establishes the no-speculative-reconnect rule (the QR ceremony runs only on an explicit "reconnect required" marker, never as a diagnostic first step) and a three-step escalation ladder with costs -- 2026-08-18 -- `knowledge/decisions/0067-steam-mobileapp-login-signature-accepted-risk-and-escalation-ladder.md`
- **0066** -- Steam storefront calls are paced inside the Adapter, not by callers: one process-wide `Steam.throttleStorefrontCall` gate (a `SemaphoreSlim` held across the interval wait and the call itself, default 1500ms from the ~200 req/5min ceiling, injectable for tests) fronts every `store.steampowered.com` call — `appdetails`, trailers, search store-meta, and the Deck-compat store page — replacing eleven independently-remembered caller-owned `Async.Sleep`s, only three of which existed -- 2026-08-18 -- `knowledge/decisions/0066-steam-storefront-throttle-is-adapter-owned.md`
- **0065** -- Steam Web API key rejection gets a typed shape (`SteamWebApiError`/`KeyRejected` via the non-throwing `Steam.tryGetOwnedGames`), degrades the Family import's owned-games supplement instead of aborting the whole import, and is attributed separately from the family token — persisted to `steam_api_key_last_error` for a standing Settings notice, cleared on save/test/next success -- 2026-08-15 -- `knowledge/decisions/0065-steam-web-api-key-typed-rejection-and-fault-isolation.md`
- **0061** -- Steam Connect QR login runs as an in-memory server session streamed to Settings over SSE; the refresh token persists in SettingsStore, and `Steam.withTokenRefresh` is production-wired with real mint/persist lambdas so family tokens self-heal, with "reconnect required" surfaced as data -- 2026-08-07 -- `knowledge/decisions/0061-steam-connect-qr-session-and-family-token-refresh-wiring.md`
- **0040** -- The still backfill for pre-existing Jellyfin rows widens `materializeMissingEpisodes`' skip predicate rather than running as a separate sweep (the Jellyfin item id is already in the batch), writing through a dedicated `backfillEpisodeStill` UPDATE that repeats `source='jellyfin' AND still_ref IS NULL` in its WHERE clause; no refetch guard — repetition is accepted because the candidate set drains itself on TMDB enrichment -- 2026-08-01 -- `knowledge/decisions/0040-jellyfin-still-backfill-lives-in-materialize-no-refetch-guard.md`
- **0039** -- Jellyfin-materialized stills use a distinct `-jellyfin.jpg` storage path, not TMDB's canonical one, so a later TMDB refresh is never short-circuited into keeping the Jellyfin bytes; the resulting post-enrichment orphan is accepted and reclaimed via the ADR-0025 orphan scan -- 2026-08-01 -- `knowledge/decisions/0039-jellyfin-still-distinct-storage-path-accepted-orphan.md`
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
- **steam-account-flag-risk-surface** -- What Mediatheca's Steam account-hijack-flag risk is made of: three independent Steam surfaces that must never be conflated (family refresh token / Web API key / unauthenticated storefront), the two halves of the suspected signature -- enumeration (fixed: ADR-0066 spacing + ADR-0069 count) and login (accepted as unfixable under ADR-0019 pt 2) -- the no-speculative-reconnect rule, and the escalation ladder -- `contexts/integration/concepts/steam-account-flag-risk-surface.md`
<!-- concepts:end -->

## Pointers

- BC README (ubiquitous language, invariants): `README.md`
