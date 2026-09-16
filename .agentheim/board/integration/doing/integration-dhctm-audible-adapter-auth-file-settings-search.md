---
id: integration-dhctm
title: Audible adapter and Settings card — an imported audible-cli auth file (never a login or device registration, ADR-0074) with refresh-token → access-token minting, a "Test connection" that names the customer and marketplace, unauthenticated catalog search and product detail with Audnexus as metadata fallback, and `addBookFromAudible`
status: doing
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, design-system-001-formalize-styleguide]
blocks: [integration-jjvg2, integration-wmqn3, books-g7g1j]
tags: [books, audible, adapter, settings, auth-file, audnexus, search]
related_adrs: [0074, 0070, 0065, 0011, 0043, 0045, 0076]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [integration-v0xmv, integration-qb7tk, integration-r8kwd]
---

## Why

Audible is the builder's audiobook source and the primary source of *listening progress*. There is
no official API; the internal one needs a device-registered client for anything personal. ADR-0074
settles how Mediatheca gets there without ever performing the registration itself: the user runs
`audible-cli quickstart` on their own machine and pastes the resulting auth file. This task is the
adapter, the Settings card, the search source and the add flow; the library import and progress
sync are `integration-jjvg2`.

## What

**`src/Server/Audible.fs`** (adapter block of `Server.fsproj`):
- `AudibleAuthFile` decoded from the pasted JSON: `adp_token`, `device_private_key`, `access_token`,
  `refresh_token`, `expires` (float epoch), `locale_code` (e.g. `"de"`, `"us"`, `"uk"`),
  `customer_info` (`name`, `user_id`), `website_cookies`, `store_authentication_cookie`,
  `device_info`. Required for acceptance: `refresh_token`, `adp_token`, `device_private_key`,
  `locale_code`, `customer_info.name`. `validateAuthFile: string -> Result<AudibleAuthFile, string>`.
- `marketplaceHost locale` — `us → api.audible.com`, `uk → api.audible.co.uk`, `de → api.audible.de`,
  `fr → .fr`, `ca → .ca`, `au → .com.au`, `it → .it`, `es → .es`, `jp → .co.jp`, `in → .in`;
  `amazonTokenHost locale` — `api.amazon.com` for `us`, `api.amazon.co.uk`/`.de`/… otherwise
  (the mkb79 `LOCALE_TEMPLATES` table — copy it verbatim from `audible/localization.py`).
- `refreshAccessToken httpClient authFile` — `POST https://{amazonTokenHost}/auth/token` with form
  body `app_name=Audible&app_version=3.56.2&source_token={refresh_token}&requested_token_type=access_token
  &source_token_type=refresh_token` (as mkb79's `_refresh_token_request_body`), decoding
  `access_token` + `expires_in`; persists `audible_access_token` and `audible_access_token_expires`
  (ISO) in `SettingsStore`. A 400/401/403 here → `Error (AuthFileRejected msg)`.
- `withAccessToken` — returns a cached token while > 5 minutes remain, else refreshes once; on a 401
  from an API call, refreshes once and retries once (the `Jellyfin.withReauthRetry` shape); a second
  rejection surfaces `AuthFileRejected`. **No code path registers a device or logs in.**
- Authenticated calls send `Authorization: Bearer {access_token}` and `client-id: 0`. (Signed-request
  auth with the RSA key is *not* implemented — bearer suffices for `/1.0/library`; note it in the
  README if a call turns out to need signing.)
- `searchCatalog httpClient host keywords` — unauthenticated `GET /1.0/catalog/products?keywords=…&
  num_results=20&response_groups=product_desc,product_attrs,media,contributors,series&image_sizes=500`
  → `AudibleSearchResult = { Asin; Title; Authors; Narrators; RuntimeMinutes: int option;
  ReleaseYear: int option; CoverUrl: string option (500 px); SeriesName: string option }`. 1 h
  in-process cache keyed by (host, keywords).
- `getProduct httpClient host asin` — unauthenticated `GET /1.0/catalog/products/{asin}?response_groups=
  product_desc,product_extended_attrs,product_attrs,media,contributors,series,rating,category_ladders
  &image_sizes=900` → `AudibleProduct = { Asin; Title; Subtitle; Authors; Narrators; Publisher;
  ReleaseDate; RuntimeMinutes; Description (publisher_summary, HTML stripped); Language; Rating;
  SeriesName; SeriesPosition; Categories (top-level names, ≤ 6); CoverUrl (900 px) }`.
- `Audnexus.getBook httpClient asin` — `GET https://api.audnex.us/books/{asin}?region={locale}`,
  fallback when `getProduct` lacks description/narrators/series; never a hard dependency (404/5xx →
  `None`). 100 req/min limit — irrelevant at our volumes, but one gate at 700 ms anyway.
- `getCustomerSummary httpClient authFile` — `GET /1.0/library?num_results=1&response_groups=product_desc`
  authenticated, returning the total count if the response carries one, else "reachable". This is the
  "Test connection" probe.
- Error wording: every auth failure message begins with the fixed prefix `"audible auth file rejected: "`
  (`Audible.authFileRejectedPrefix`), persisted to `audible_last_error`; cleared on a successful
  save/test/library call (the `steam_api_key_last_error` pattern, ADR-0065).

**Settings** (`SettingsStore` keys, `Composition.getAudibleConfig`, `Api.fs`, Shared):
- Keys: `audible_auth_file` (the JSON, secret), `audible_marketplace` (locale used for unauthenticated
  search when no auth file; default `de`), `audible_access_token`, `audible_access_token_expires`,
  `audible_last_error`.
- `IMediathecaApi`: `getAudibleStatus: unit -> Async<AudibleStatus>` (`{ Configured: bool;
  CustomerName: string option; Marketplace: string; LastError: string option }` — never the file),
  `setAudibleAuthFile: string -> Async<Result<AudibleStatus, string>>` (validates, stores, clears
  last error, derives marketplace from `locale_code`), `clearAudibleAuthFile`, `testAudibleConnection:
  unit -> Async<Result<string, string>>` (refreshes a token + `getCustomerSummary`; a rejection
  persists `audible_last_error`), `getAudibleMarketplace` / `setAudibleMarketplace`,
  `searchAudibleBooks: string -> Async<AudibleSearchResult list>` (works without an auth file),
  `addBookFromAudible: AddBookFromAudibleRequest -> Async<Result<AddBookOutcome, string>>` with
  `{ Asin; SkipDuplicateCheck }`: `getProduct` (+ Audnexus fallback) → `AddBookRequest` (Title,
  Authors, Year from release date, CoverUrl 900 px, Subjects = Categories, `Format = Audiobook`,
  ExternalIds = `[AudibleAsin asin]`) → `addBook` → on `Book_added`, `upsertBookMetadata` with
  description, runtime_minutes, narrators, series, publisher, published_date, average_rating,
  language, `source = "audible"`.
- Settings page (`Pages/Settings/{Types,State,Views}.fs`): an **Audible** `integrationCard` in the
  Integrations grid (after qBittorrent) with: a marketplace select; a paste textarea for the auth
  file with the three-line procedure (`pipx install audible-cli` → `audible quickstart` on your own
  machine → paste `~/.audible/<profile>.json` here); Save / Test connection / Clear; a status badge
  (Not configured / Connected as *Name* (de) / Rejected); the standing `audible_last_error` notice
  in the same warning style as `SteamFamilyTokenRejected`. The textarea never re-displays a stored
  file (placeholder "auth file stored — paste a new one to replace").

## Acceptance criteria

- [ ] `AudibleTests.fs`: `validateAuthFile` accepts a fixture with every field, rejects one missing
      `refresh_token` naming the field, rejects non-JSON; `marketplaceHost "de"` = `api.audible.de`.
- [ ] `refreshAccessToken` sends the five form fields above to `https://api.amazon.de/auth/token` for
      `locale_code = "de"` (recording handler) and stores token + expiry; a 401 response returns
      `AuthFileRejected` and persists `audible_last_error` starting with the fixed prefix.
- [ ] `withAccessToken` reuses a cached token with > 5 min left (zero token calls), refreshes once on
      expiry, and on a 401 from the API call refreshes once and retries once — a second 401 surfaces
      `AuthFileRejected` (exactly one refresh, asserted).
- [ ] `grep -rn "auth/register\|from_login\|device_registration" src/Server/Audible.fs` is empty
      (no registration path exists). This grep is the verification — do not attempt a reflection-based
      unit test asserting "no member named register/login" (F# module functions compile to static
      methods on a generated class and such a test would be fragile/over-engineered for what the grep
      already proves); the CI/review step running the grep is sufficient. [human-eye or CI grep step]
- [ ] `searchAudibleBooks "dune"` against a captured unauthenticated fixture decodes asin, title,
      authors, narrators, runtime, 500 px cover; works with no auth file stored.
- [ ] `addBookFromAudible` creates a book with `Format = Audiobook`, `AudibleAsin` linked, cover at
      `posters/book-{slug}.jpg`, and `book_metadata_cache.runtime_minutes`/`narrators` filled; a
      second call returns `Duplicate_found`; when the product lacks a description the Audnexus fixture
      fills it.
- [ ] `getAudibleStatus` never returns the auth file or any token (assert the DTO has no such field
      and the JSON of the stored setting never appears in the response).
- [ ] Settings: saving a malformed file shows the validation message; a valid file flips the badge
      to "Connected as {name} ({marketplace})"; Test connection with a stubbed 401 shows the standing
      "paste a fresh auth file" notice. Client test (`Pages/Settings/AudibleAuthFile.test.fs`) covers
      the reducer for the three states.
- [ ] The card reads as one of the existing integration cards — same chrome, badge and button
      treatment as Jellyfin / qBittorrent. [human-eye]
- [ ] `npm test`, `npm run test:client` green; `npm run build` succeeds.

## Notes

- **Doctrine:** ADR-0074 (this task's contract), ADR-0070 (why no login ever), ADR-0065 (typed
  rejection + standing notice), ADR-0011 (one re-auth-and-retry seam). If anything in the mkb79
  sources suggests bearer auth is insufficient for `/1.0/library`, implement the signed-request
  variant (`adp_token` + RSA-SHA256 over `method\npath\ndate\nbody\nadp_token`) — the auth file
  carries what it needs; do not fall back to any login.
- Research report §2 ("Refreshing an access token from an auth file") has the token endpoint, body
  fields and the auth-file field list, cited to mkb79's `auth.py`; §3 has Audnexus; the review gate
  verified unauthenticated catalog search live against `api.audible.com` and `api.audible.de`.
- Prior art: `integration-qb7tk` (Settings card shape), `integration-v0xmv` (the deletion this
  design must never re-create), `integration-r8kwd` (typed rejection wording).
- The `Composition.fs` env seeding pattern (`TMDB_API_KEY` → setting) is **not** extended to the
  auth file — a multi-KB JSON secret does not belong in an env var; Settings only.
- Locale table: copy mkb79 `localization.py`'s `LOCALE_TEMPLATES` (country_code → domain,
  market_place_id) rather than guessing; the review gate confirmed `api.audible.de` live.
- **Scheduling note (added during refinement, 2026-09-16):** this task now `depends_on`
  `integration-c8d4x` — both tasks independently append new `IMediathecaApi` members near the same
  tail of the interface, matching `Api.fs` `create`-record fields, and a new `getXConfig` function in
  `Composition.fs`'s adapter-config cluster (~line 188–237); running them in the same parallel batch
  risks a manual-merge conflict at squash time. Land your Settings-grid `integrationCard` block after
  wherever `c8d4x` (Open Library — headless, no card) leaves the grid; since Open Library has no
  Settings card, in practice this just means placing Audible's card after qBittorrent's as already
  specified. **`integration-wmqn3` (Goodreads) now `depends_on` this task** because its own spec
  positions its card "after Audible" in the Integrations grid — land your card cleanly so that
  positioning is unambiguous for the next worker.
