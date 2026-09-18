# integration -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 1
- **Doing:** 0
- **Done:** 30
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **integration-fn3yx** — Audible sync decides on listened minutes and calculates the percent — position comes from `last_position_heard`, not the library listing's unreliable `percent_complete`; fixes the decoder that reads that endpoint at the wrong nesting level (reverses ADR-0082's "the sync never fetches last-listened"). (feature) — `todo/integration-fn3yx-audible-sync-decides-on-minutes-percent-calculated.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **integration-dvbjp** — The nightly Audible sync creates a Book for every unmatched library ASIN (same create path as the import, then an ordinary observation) and "Import library" becomes a true one-time bootstrap — stamped `audible_library_imported_at` on its first populated run, refused by the API and hidden by the Settings card afterwards (feature) — `done/integration-dvbjp-nightly-sync-creates-unmatched-books-import-one-time.md`
- **integration-dtdbb** — Audible priors carry the last-listened day — import and nightly sync fetch `last_position_heard` (`GET /1.0/content/{asin}/metadata`) only for books with no Audible progress row yet, date the prior (and so the finished date) to Audible's `last_updated`, use `position_ms` for the position, and "Import library" repairs the import-day observations written before priors existed (feature) — `done/integration-dtdbb-audible-last-listened-date-for-priors.md`
- **integration-sfmxg** — Remove the Goodreads integration — delete the adapter, the shelf/progress sync job, the Settings card and the API surface, and drop the Goodreads progress source and external id from the Book model; Audible and Open Library remain the only book sources (refactor) — `done/integration-sfmxg-remove-goodreads-integration.md`
- **integration-jjvg2** — Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice (feature) — `done/integration-jjvg2-audible-library-import-and-progress-sync.md`
- **integration-y2ak4** — Goodreads reading progress from the public user-status feed — parse "is on page N of M of Title" / "is N% done with Title" / "finished reading" items from `user_status/list/{id}?format=rss`, join them to currently-reading books by normalized title, and emit `Observe_reading_progress` (source Goodreads) as part of the shelf sync, idempotent across runs (feature) — `done/integration-y2ak4-goodreads-reading-progress-from-status-feed.md`
- **integration-wmqn3** — Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN (feature) — `done/integration-wmqn3-goodreads-shelf-sync-settings-card.md`
- **integration-dhctm** — Audible adapter and Settings card — an imported audible-cli auth file (never a login or device registration, ADR-0074) with refresh-token → access-token minting, a "Test connection" that names the customer and marketplace, unauthenticated catalog search and product detail with Audnexus as metadata fallback, and `addBookFromAudible` (feature) — `done/integration-dhctm-audible-adapter-auth-file-settings-search.md`
- **integration-c8d4x** — Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled (feature) — `done/integration-c8d4x-open-library-adapter-search-isbn-covers.md`
- **integration-mqsd3** — "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API (feature) — `done/integration-mqsd3-remove-local-copy-from-jellyfin-and-qbittorrent.md`
- **integration-r4vzm** — Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0071) (feature) — `done/integration-r4vzm-local-copy-removal-server-flow.md`
- **integration-qb7tk** — qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on (feature) — `done/integration-qb7tk-qbittorrent-adapter-and-settings.md`
- **integration-v0xmv** — Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again (refactor) — `done/integration-v0xmv-remove-steam-connect-qr-login-manual-token-only.md`
- **integration-zwnh4** — Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert (bug) — `done/integration-zwnh4-steam-connect-stable-device-identity.md`
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


## Pointers

- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/integration/INDEX.md`
