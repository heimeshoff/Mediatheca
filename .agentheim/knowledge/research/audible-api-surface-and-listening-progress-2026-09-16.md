---
topic: Audible API surface for catalog metadata and listening progress (official, unofficial, and Audnexus)
date: 2026-09-16
requested_by: model
related_tasks: []
---

# Research: Audible API surface and listening-progress access

## Question

For a planned Audible integration (search catalog, show a details page, track per-title listening
progress on a cadence or on demand): what official API exists, what the unofficial
`mkb79/audible` / `audible-cli` API surface looks like (endpoints, auth, response fields),
what Audnexus offers as a metadata-only alternative, what non-login ways exist to get listening
progress, what the account-risk profile is, and how to get cover art. This sits next to
ADR-0070 (Mediatheca "never performs a Steam login or mints a Steam token" after a Valve ban
threat) — the same honesty standard applies to describing whether an Audible integration would
require the server to perform a comparable Amazon device-registration ceremony.

## Summary

- **There is no official Audible API for catalog metadata or personal library/listening data.**
  Amazon's Product Advertising API (PA-API 5.0) covers retail products broadly but has no
  Audible-specific program; nothing surfaced indicating Audible-branded PA-API access as of
  2026 [1][8]. Everything below is unofficial, reverse-engineered, or metadata-only.
- **The unofficial API (`api.audible.com`, per-marketplace hosts) is real, actively documented by
  the community, and does return exactly the fields wanted** — `percent_complete`, `is_finished`,
  and `listening_status` are confirmed `response_groups`/fields on `/1.0/library`; `purchase_date`
  lives under `order_details`; `runtime_length_min` is a plain product field surfaced via the
  `media` response_group, not a response_group itself [2][3]. **Catalog search is confirmed
  credential-free**: a live, unauthenticated fetch of `GET /1.0/catalog/products` on both
  `api.audible.com` and the German marketplace host `api.audible.de` returned full results (asin,
  title, authors, narrators, runtime, cover images) with no login [2A][2B] — this is stronger than
  "plausible but unconfirmed." Library/listening-status endpoints still require full authentication.
- **Getting authenticated at all requires a real Amazon OAuth Authorization-Code-with-PKCE flow
  culminating in a POST to Amazon's `/auth/register` device-registration endpoint**, which mints a
  new persistent device identity on the account (refresh token, RSA private key, `adp_token`) —
  this is not equivalent to Steam's "paste an already-active browser token," and doing it from a
  server is the same category of act ADR-0070 forecloses for Steam [5][6]. This is the single
  most decision-relevant finding.
- **Audnexus (`api.audnex.us`) is a free, no-auth, ASIN-keyed metadata API** — cover image,
  narrators, series, genres, description, runtime, all present — with no title/keyword search for
  books (only author-name search) and a 100 req/min limit; it is alive and maintained in 2026,
  though its own maintainer is redirecting new development effort to a successor, AudiobookDB,
  while keeping Audnexus itself online [7][11][12].
- **No progress source avoids the login/device-registration problem cleanly.** `audible-cli`'s
  library export needs the same device-registered auth file; the Audible website's authenticated
  library page can be scraped with just a session cookie (`x-main`) without device registration,
  but community write-ups only confirm title/author/cover this way, not percent-complete —
  single-source and unconfirmed for progress data [9][10]. Amazon's personal "Request My Data"
  export includes per-session listening duration but is a manual, days-later download, not an
  on-demand or cadence-refreshable API [13].
- **Risk data is thin.** Token lifetimes are well documented (access token ~60 min, refresh token
  durable/long-lived, obtained only via device registration) [3][5], but concrete, dated reports
  of Amazon banning accounts specifically for `mkb79/audible`-style unofficial API or device
  registration use did not surface in this pass — treat the risk as directionally real (Amazon
  polices automated/non-browser traffic against Audible generally per scraping guidance [14]) but
  not evidenced with incident reports the way the Steam case (ADR-0067/0070) was.

## Findings

### 1. Official API: what exists and what doesn't

No search or fetch surfaced an Audible-branded Product Advertising API, nor any official Amazon
endpoint for a user's personal audiobook library or listening progress. The only official-Amazon
surface touching Audible in 2026 is advertising tooling (Sponsored Products/Brands for Audible
titles, an Amazon Ads feature, not a data API) [1]. Amazon's general Product Advertising API 5.0
exists for retail product data/images but nothing found ties it to Audible catalog or library data
specifically [8]. The `mkb79/audible` docs state plainly: "There is currently no publicly
available documentation about the Audible API" [2] — everything usable is unofficial and
reverse-engineered from the mobile/web apps.

**Conclusion: for both catalog metadata and listening progress, there is no official API path in
2026.** Anything built has to go through the unofficial internal API, a metadata-aggregator like
Audnexus, or scraping/export.

### 2. The unofficial internal API (`mkb79/audible` / `audible-cli` surface)

**Base URLs.** The docs don't publish a literal table of per-marketplace hosts in the pages
fetched, but confirm the pattern is one host per Amazon marketplace (store IDs like
`AN7V1F1VY261K` are enumerated) [2]; the well-known convention from this ecosystem (used
consistently across `mkb79/audible`, `audible-cli`, and derivative tools like `AudibleApi` for C#)
is `api.audible.com` (US), `api.audible.co.uk`, `api.audible.de`, `api.audible.fr`, etc., mirroring
`audible.<tld>`. **This is now directly confirmed, not just inferred**: a live unauthenticated
fetch of `GET https://api.audible.de/1.0/catalog/products?keywords=dune&num_results=1&response_groups=media`
returned a real, German-language product record (title "Der Wüstenplanet 1", narrators Mark Bremer
and Uta Dänekamp, `total_results: 2519`), proving the `api.audible.<tld>` host convention is real
and that at least this host also serves unauthenticated catalog queries [2B].

**Authentication.** Per `mkb79/audible`'s own docs [5][6]:
- Clients authorize via **OpenID Authorization Code Flow with PKCE** against Amazon.
- The authorization code is then used to call Amazon's **device-registration endpoint
  (`/auth/register`)**, using cookies obtained during the Amazon login plus the access token from
  the OAuth step.
- Device registration returns: an **access token** (~60 min lifetime), a **refresh token**
  (durable — used to mint new access tokens; obtainable *only* via device registration), an
  **RSA private key**, and an **`adp_token`** — plus device type/serial and marketplace/customer
  id. The RSA key + `adp_token` pair is used for "signed request" auth (SHA256-signed digest,
  described as giving "unrestricted access to the Audible API"); alternatively a bearer token
  (access token + client id) can be used for standard calls [5].
- `Authenticator.from_login()` does this with a username/password prompt (handling CAPTCHA/2FA/CVF
  callbacks); `Authenticator.from_login_external()` prints a login URL, the user completes login
  in their own browser, and pastes back the final (error) redirect URL — **but this still drives
  the same device-registration call from the calling program**, it only relocates where the
  *browser* part of the OAuth dance happens, not where the device-registration API call happens
  [6]. This is the key point for risk framing below.
- A **"pre-registered device" is required for essentially everything behind `/1.0/library`** —
  there is no way to read a user's library or listening status without a device-registered
  refresh/access token; this is corroborated by every tool in this space (Libation, OpenAudible,
  `audible-cli`) performing this same registration ceremony rather than working around it [5][6].

**Catalog search.** `GET /1.0/catalog/products?keywords=...` (plus `title`, `author`, `narrator`,
`category_id` filters) has a **confirmed credential-free public slice** — not just a plausible
one. A live, unauthenticated `WebFetch` of
`https://api.audible.com/1.0/catalog/products?keywords=dune&num_results=3&response_groups=product_desc,media,contributors`
returned three full Dune audiobook records (asin, title, subtitle, authors, narrators,
`runtime_length_min`, `product_images`, publisher, description, codecs) with zero authentication
[2A]. A second unauthenticated fetch against the German marketplace host,
`https://api.audible.de/1.0/catalog/products?keywords=dune&num_results=1&response_groups=media`,
also succeeded, returning a German-language record and `total_results: 2519` [2B]. This
supersedes the earlier "plausible but not independently confirmed" hedge from a web-search
synthesis [4]: **basic keyword catalog search plus common metadata `response_groups` genuinely
requires no login.** What remains unconfirmed is whether *every* `response_groups` value (e.g.
`price`, `sample`) is available unauthenticated, or only a subset — the docs' blanket "most calls
need to be authenticated" [2] and this pass's two successful anonymous fetches aren't fully
reconciled; treat product-level completeness (not basic search) as the remaining open question.

**Product detail.** `GET /1.0/catalog/products/{asin}` (confirmed exact path, e.g.
`/1.0/catalog/products/B002V02KPU`) [4], plus `.../{asin}/reviews` and `.../{asin}/sims`
(similar items) [2]. Controlled via `response_groups` — the **verbatim list for this endpoint**,
directly re-fetched and quoted from the docs page, is: `contributors, media, price, product_attrs,
product_desc, product_details, product_extended_attrs, product_plan_details, product_plans,
rating, sample, sku, series, reviews, relationships, review_attrs, category_ladders,
claim_code_url, provided_review, rights, customer_rights, goodreads_ratings` [2] — notably this
**includes `series`**, which the details page needs. (Corrected in this revision: an earlier draft
attributed a shorter list — `sku, product_attrs, rating, product_extended_attrs, media, sample,
product_plans, product_plan_details, badges, relationships, customer_rights, product_desc,
contributors` — to this endpoint. That shorter list is actually documented under a *different*
`GET /1.0/catalog/products` directive on the same docs page — the batch/`asins`-lookup variant of
the search endpoint, not `/{asin}` product detail. The two directives are distinct and
non-identical; `/{asin}`'s own list is the longer one quoted above.) The `/{asin}` endpoint's docs
leave `image_sizes` undocumented — no enumerated pixel-size token list appears on that query line
for this endpoint. The `1215, 408, 360, 882, 315, 570, 252, 558, 900` enumeration (see Cover art,
section 6) belongs to that other `GET /1.0/catalog/products` directive, not to `/{asin}`; those
tokens are also not an exhaustive whitelist in practice: `audnexus`'s own Audible client requests
`image_sizes=500,1024` against `api.audible.<tld>/1.0/catalog/products` and gets results, i.e. the
API accepts sizes beyond the documented enumeration [2C].

**Library + listening status.** `GET /1.0/library` (also `GET /1.0/library/{asin}`) [2]. The
**verbatim `response_groups` list for this endpoint**, re-fetched and quoted directly from the
docs page, is: `contributors, customer_rights, media, price, product_attrs, product_desc,
product_details, product_extended_attrs, product_plan_details, product_plans, rating, sample, sku,
series, reviews, ws4v, origin, relationships, review_attrs, categories, badge_types,
category_ladders, claim_code_url, in_wishlist, is_archived, is_downloaded, is_finished,
is_playable, is_removable, is_returnable, is_visible, listening_status, order_details,
origin_asin, pdf_url, percent_complete, periodicals, provided_review` [2]. This includes
`percent_complete`, `is_finished`, `listening_status`, `is_downloaded`, `is_returnable`,
`is_playable`, and `order_details` (carries `purchase_date`/similar) — all directly relevant to
progress tracking. **Correction: `last_position_heard` is not part of this list and does not
appear on `/1.0/library` at all.** On the same docs page, `last_position_heard` appears only as a
`response_groups` option for `POST /1.0/content/{asin}/licenserequest`, and separately as a query
parameter on `GET /1.0/content/{asin}/metadata` [2] — i.e. if the integration ever needs the exact
playback *position* (not just `percent_complete`), that lives behind the license-request/streaming
flow, not the library listing. `runtime_length_min` and `percent_complete`/`is_finished` were
additionally corroborated by a targeted web-search cross-check [3], but that source is secondary
to the directly-quoted docs page [2]. Sort/paging params: `num_results` (max 1000 for library, max
50 for search — per docs page), `sort_by` (`-Author, -Length, -Narrator, -PurchaseDate, -Title`
and unprefixed ascending variants), `status` (`Active`/`Revoked`) [2].

**Stats endpoint.** Confirmed to exist as a *group* of endpoints, not a single `/1.0/stats`:
`GET /1.0/stats/aggregates`, `GET /1.0/stats/status/finished` (also `POST`), and
`PUT /1.0/stats/events` [2]. No response schema for `/1.0/stats/aggregates` was captured in this
pass — flag as **endpoint path confirmed, response shape unconfirmed**.

**Unauthenticated vs authenticated, overall.** The docs' blanket statement is "most calls need to
be authenticated" [2], but this is now known to be an overstatement for basic catalog search
specifically: two independent live, unauthenticated fetches (US and German marketplace hosts)
both returned full catalog results [2A][2B]. **Library/listening-status endpoints unambiguously
require authentication** — nothing suggests otherwise anywhere in this research, and no
unauthenticated fetch in this pass touched `/1.0/library`.

**Refreshing an access token from an auth file.** Decision-critical for this project, since the
plan is to import a user-generated `audible-cli`/`mkb79/audible` auth file rather than ever running
the device-registration ceremony itself. Read directly from `mkb79/audible`'s source
(`src/audible/auth.py`) [2D]:
- `refresh_access_token(refresh_token, domain, with_username=False)` POSTs to
  `f"https://api.{target_domain}.{domain}/auth/token"`, where `target_domain` is `"amazon"` by
  default or `"audible"` if `with_username=True` (i.e. the account was registered via an Audible
  username/password rather than an Amazon one) — for a US, Amazon-login account this resolves to
  `https://api.amazon.com/auth/token`.
- The POST body (form-encoded), quoted from `_refresh_token_request_body()`:
  ```
  {
    "app_name": "Audible",
    "app_version": "3.56.2",
    "source_token": <refresh_token>,
    "requested_token_type": "access_token",
    "source_token_type": "refresh_token",
  }
  ```
  No `grant_type` field is sent; the new access token is valid 60 minutes per the function's own
  docstring, consistent with the token-lifetime figure already cited in section 5.
- The **auth-file JSON** written by `Authenticator.to_dict()` — i.e. the exact shape of a
  hand-generated `audible-cli`/`mkb79/audible` auth file this project would import — contains:
  `website_cookies`, `adp_token`, `access_token`, `refresh_token`, `device_private_key`,
  `store_authentication_cookie`, `device_info`, `customer_info`, `expires`, `locale_code`,
  `with_username`, `activation_bytes` [2D]. Practically: importing this file and calling
  `refresh_access_token` with its `refresh_token` and `locale_code`-derived domain is sufficient to
  mint new 60-minute access tokens indefinitely, without Mediatheca itself ever performing the
  OAuth/device-registration ceremony described earlier in this section — the registration event
  happened once, on the user's own machine, before the file was handed over. This is the
  structural fit with ADR-0070's "paste an already-active token" posture; the caveat is that the
  refresh token itself is the durable secret, and (per section 5) no source found documents its
  expiry, so it must be treated as capable of going stale/being revoked without warning.

### 3. Audnexus (`api.audnex.us`)

Fetched directly from `https://audnex.us/` (interactive docs) [11]:
- **Base URL:** `https://api.audnex.us`. Current API version at time of fetch: `1.8.0`.
- **Endpoints:** `GET /books/{ASIN}`, `DELETE /books/{ASIN}`; `GET /books/{ASIN}/chapters`;
  `GET /authors` (name search), `GET /authors/{ASIN}`; `GET /health`.
- **Book fields returned:** title, authors, narrators, copyright year, description
  (plain + "formatted"/HTML summary), format type, genres, cover image URL, ISBN, language,
  literature classification, publication date, runtime (minutes), rating — i.e. everything the
  planned details page needs except live listening progress (which is inherently
  user-account-specific and out of scope for a public aggregator) [11].
- **Search: confirmed no book title/keyword search** — only ASIN-keyed book lookup. Author search
  by name exists (`GET /authors?name=...`), corroborated independently by a second source
  describing Audnexus's `Search()` as "returns results only when q.ProviderIDs['asin'] is set" —
  i.e. book search is ASIN-only, matching the finding [7][11]. **This is a repeated,
  cross-sourced claim, not single-source.**
- **Regions:** `region` query param supports US (default), UK, CA, AU, DE, ES, FR, IN, IT, JP —
  useful if Mediatheca ever needs non-US marketplace metadata [11].
- **Rate limit:** 100 requests/minute per source, corroborated by both the GitHub repo's config
  (`MAX_REQUESTS` default 100/min) and an independent DeepWiki-sourced summary [7][11].
- **Alive in 2026 / maintenance status:** the GitHub repo shows active 2025–2026 activity (2,510+
  commits on `develop`, open issues/PRs, recent workflow files) [7]. However, **the maintainer has
  publicly signaled a shift of primary effort to a successor project, AudiobookDB** ("proper
  book/release separation, moderated community contributions, fast search... inspired by
  TheMovieDB and MusicBrainz"), with a stated possibility of eventually turning Audnexus into "more
  of a seeder proxy... for multiple platforms" [12]. Audnexus itself is not deprecated or shut
  down as of this research, but **AudiobookDB is the forward-looking bet if this integration has
  a multi-year horizon** — worth a follow-up look before committing long-term, not before building
  now.
- **License:** GPL v3 [7].

### 4. Alternatives to a server-side Amazon login for progress

Every option surveyed, ranked from "needs the least" to "needs the most":

1. **Amazon "Request My Data" personal export** [13]: sign in on Amazon's own site, request a
   data package (Audible-inclusive), wait "a few days," download a file containing session-level
   listening data — date, product name, listen duration in milliseconds, stop reason, device
   type. This needs the user's own Amazon login (in their own browser, once) but **no
   device-registration ceremony, no API key, no ongoing credential held by any app** — the
   tradeoff is it's a manual, latent, batch export, not something refreshable "on a cadence or on
   demand" as the integration wants. Field names beyond the ones quoted weren't independently
   verified against Amazon's own docs page in this pass (search-summary sourced) — **single-source
   claim**.
2. **Cookie-based scraping of the authenticated library page** (`audible.com/library/titles`),
   using just a copied `x-main` session cookie — no OAuth flow, no device registration [9]. One
   detailed community write-up (a dev.to post building exactly this) confirms: the `x-main`
   cookie alone is sufficient to page through the library and pull **title, author, cover image,
   product URL** — but **the article contains no mention of percent-complete or listening-position
   data being present on that page/via that method** [9]. A second, more generic source notes
   Audible renders much of its product grid client-side and that cookie validity for the library
   section is inconsistent ("sometimes triggers sign-in redirects despite validity") [10][14].
   **This is the one path structurally analogous to ADR-0070's "paste a browser-obtained token"
   pattern** (a cookie the user's own browser already holds, no registration call performed by
   Mediatheca) — but it is unconfirmed whether it actually surfaces `percent_complete`, and it is
   fragile/scraping-shaped rather than a stable JSON API.
3. **`audible-cli library export`** [command confirmed via multiple sources, exact command:
   `audible library export --output library.json`] [search-summary]. This needs full
   device-registered auth (`audible quickstart` interactive OAuth+device-registration setup, or
   `audible manage auth-file add`; CI-friendly minimal credential set is `adp_token` +
   `device_private_key`) [README fetch, partially confirmed] — i.e. it sits on top of the exact
   device-registration ceremony from section 2, just run once by a human via CLI rather than by a
   server continuously. One web-search synthesis claims the tool exposes "80 fields" via a `-l`
   list option, with sensible defaults `ASIN, TITLE, AUTHORS, DURATION, PURCHASE_DATE` — **this
   specific figure and default-column list is single-source (a search-engine summary, not a
   directly fetched/quoted doc page)** and should be verified against `audible library export -h`
   before being relied on for field names like `percent_complete`.
4. **Full unofficial API via `mkb79/audible`/`audible-cli`, run server-side on a cadence** — the
   integration this research question is implicitly evaluating. Needs the full OAuth +
   device-registration ceremony described in section 2, performed and then *maintained* (refresh
   token renewal) by the server.
5. **How comparable open-source projects do it**: **Libation** performs the same device
   registration Amazon/Audible OAuth flow (`login-external`, i.e. an external-browser variant of
   the same ceremony), offering two device profiles (`CurrentAndroid` default, experimental
   `Mkb79IPhone`); "Libation... never saves your username or password, but a credential token is
   saved so you can stay connected until you log out" [6-summary]. **OpenAudible** likewise uses
   the `mkb79/audible`-style OAuth+device-registration flow (its own `AudibleScraper.java` file
   exists alongside true API calls, suggesting a hybrid of API + HTML scraping) [15]. Neither tool
   found in this research avoids device registration entirely for library/progress access — **no
   surveyed tool that reads listening progress avoids the device-registration ceremony**, only the
   raw-cookie library-page scrape (#2 above) plausibly does, and only for non-progress fields
   confirmed so far.
6. **Audiobookshelf**: uses Audible (and Audnexus) purely as a **metadata provider** for matching
   owned/self-hosted audio files to rich metadata — it does not sync a user's Audible purchase
   library or listening progress from Amazon at all; ABS's own listening-progress tracking is
   entirely internal (its own player position, synced over its own Socket.IO API across ABS
   clients) [16]. This confirms Audiobookshelf is not a precedent for "read Amazon's listening
   progress" — it deliberately doesn't do that.

**None of the surveyed non-login paths cleanly deliver on-demand/cadence-refreshable
`percent_complete` without either (a) a device-registration ceremony somewhere, even if run by
a human via CLI rather than the server, or (b) an unconfirmed cookie-scrape that nobody has
documented as actually returning progress.**

### 5. Risk profile

- **Token lifetimes** (well-corroborated, appears identically worded across multiple docs pages):
  access token expires after 60 minutes; refresh token is durable/long-lived and is obtainable
  only through device registration [5][6]. No explicit refresh-token expiry duration (e.g. "90
  days" or "1 year") was found in any source fetched — Amazon doesn't appear to publish this, and
  the unofficial docs don't state a number either. **This should be treated as unknown/unbounded
  until observed empirically**, which itself is a risk (you cannot reason about renewal cadence
  without running it against a real account first — the same category of "test in production
  against the only account you have" problem ADR-0070 flags for Steam).
- **Account-ban reports for `mkb79/audible`/`audible-cli`/Libation-style unofficial API use**:
  **no concrete, dated incident reports surfaced in this research pass.** This is a meaningful
  negative finding, not an assurance — it did not rule out risk, it just means this pass's
  searches (Reddit, general web) didn't turn up documented cases the way ADR-0067's Steam
  research did. Given Libation and OpenAudible have existed for years with this exact
  device-registration approach and remain active open-source projects with (implicitly) users who
  haven't reported mass bans, the *ecosystem* signal is milder than Steam's ("certain brand of
  account hijacking" wording), but this is inference from absence of evidence, not a
  confirmed safety claim.
- **Rate limiting is real and documented, but for downloads/Plus catalog use, not clearly for API
  metadata reads**: Libation's own docs warn about needing to "wait 24 to 48 hours" after being
  rate-limited, attributing it partly to "Audible also rate-limits heavy Plus use" [6-summary] —
  this is about downloading audio content, not about calling `/1.0/library` for progress, so it
  may not transfer directly to a lightweight per-title progress poll.
- **General scraping guidance** (for the cookie/HTML path) explicitly warns that "Audible
  challenges or blocks traffic that does not look like a real browser" and recommends against
  going near login-gated data at all for compliance reasons [14] — this is vendor/practitioner
  advice, not an Amazon policy citation, but it's a second independent voice pointing the same
  direction as ADR-0070's caution.
- **Bottom line for the ADR-0070 parallel**: unlike Steam (where a stable "paste the browser's
  already-active `access_token`" mode exists and was proven incident-free across all three Steam
  alerts [ADR-0070]), **Audible has no equally lightweight non-registration equivalent that is
  confirmed to expose listening progress.** The closest analogue (`x-main` cookie scrape of the
  library HTML) is unconfirmed for progress data and is scraping-fragile. Any listening-progress
  feature that goes further than "manually paste a `library export` JSON the user generated once,
  themselves, via `audible-cli` on their own machine" pushes into the same category of act
  ADR-0070 closed off for Steam: a program (server or otherwise) performing an Amazon
  authorization/device-registration ceremony. Whether the server does it directly, or a CLI on the
  user's own machine does it and the server just ingests the resulting export file, is the
  meaningful design fork — the latter keeps the registration ceremony fully outside Mediatheca's
  process and network footprint, at the cost of it being a manual, human-run refresh rather than
  an automatic cadence.

### 6. Cover art

- **Catalog/product and library responses carry a `product_images` field whose `image_sizes`
  parameter accepts pixel tokens — but the two endpoints' documented lists differ, and an earlier
  draft of this report conflated them.** `GET /1.0/catalog/products` documents `1215, 408, 360,
  882, 315, 570, 252, 558, 900` (no `500`); `GET /1.0/library` documents the same list plus `500`
  [2]. In practice the token list is **not an exhaustive whitelist**: `audnexus`'s Audible client
  requests `image_sizes=500,1024` against `api.audible.<tld>/1.0/catalog/products` (the endpoint
  whose docs omit both those values) and gets valid results [2C] — so treat the documented lists
  as "known-good, not the full set of accepted values," and feel free to request `1024` or other
  sizes not in either enumerated list. Request the sizes needed and the API returns ready-to-use
  URLs, no manual URL construction from the ASIN required. This was independently confirmed live:
  the unauthenticated Dune catalog-search fetch above returned a `500`px `product_images` URL by
  default even without an explicit `image_sizes` param [2A].
- **The underlying CDN URL pattern is Amazon's general product-image pattern**:
  `https://m.media-amazon.com/images/I/{IMAGE_ID}.{ext}`, with a size/crop modifier segment (e.g.
  `._SL500_`, `._AC_UY218_`) insertable before the extension; removing everything between the two
  dots (the modifier segment) yields the unmodified/original-resolution image [general PA-API
  image-URL convention, corroborated by a Product Advertising API 5.0 docs page and independent
  practitioner write-ups] [8][17]. **Audnexus also returns a ready cover image URL directly in its
  `/books/{asin}` response** [11], which is likely the simpler, no-auth path to get a cover for
  the details page rather than deriving one from the unofficial catalog API.
- **Official minimum-quality note (ACX cover art spec, not the API):** Audible's own creator-facing
  cover art requirements mandate at least 2400×2400px, true square, ≥72dpi [18] — useful context
  for how large a "full size" cover request should reasonably be, though this is a submission
  spec, not evidence of what the delivered CDN image size actually is.

## Sources

1. [Authors gain Amazon ad slots for Audible titles in the US](https://ppc.land/authors-gain-amazon-ad-slots-for-audible-titles-in-the-us/) — Aug 28 2026 news; confirms only ad-related official Amazon/Audible surface found, not a data API.
2. [External Audible API — audible documentation](https://audible.readthedocs.io/en/latest/misc/external_api.html) — mkb79/audible docs; primary source for endpoint list, response_groups, image_sizes, sort params. Re-fetched directly and quoted verbatim in this revision to correct the `/1.0/library` vs `/1.0/catalog/products` `response_groups`/`image_sizes` conflation from the prior draft, and to confirm `last_position_heard` belongs to `POST /1.0/content/{asin}/licenserequest` and `GET /1.0/content/{asin}/metadata`, not `/1.0/library`.
2A. Live `WebFetch` of `https://api.audible.com/1.0/catalog/products?keywords=dune&num_results=3&response_groups=product_desc,media,contributors`, performed 2026-09-16, unauthenticated — returned three full Dune product records (asin, title, authors, narrators, `runtime_length_min`, `product_images`). Primary-source evidence (a live API response), not a secondary write-up. Confirms credential-free catalog search.
2B. Live `WebFetch` of `https://api.audible.de/1.0/catalog/products?keywords=dune&num_results=1&response_groups=media`, performed 2026-09-16, unauthenticated — returned a German-language product record and `total_results: 2519`. Confirms the `api.audible.<tld>` per-marketplace host convention and that the German host also serves unauthenticated catalog queries.
2C. [audnexus ApiHelper.ts](https://raw.githubusercontent.com/laxamentumtech/audnexus/develop/src/helpers/books/audible/ApiHelper.ts) — shows audnexus itself requesting `image_sizes=500,1024` against `api.audible.<tld>/1.0/catalog/products`, evidence the documented `image_sizes` token list is not exhaustive.
2D. [Audible/src/audible/auth.py](https://raw.githubusercontent.com/mkb79/Audible/master/src/audible/auth.py) — mkb79/audible source code (primary/canonical, not docs prose); `refresh_access_token()`, `_refresh_token_request_body()`, and `Authenticator.to_dict()` quoted directly for the token-refresh POST fields/URL and the auth-file JSON field names.
3. Web-search cross-check on `api.audible.com` `/1.0/library` `response_groups` (query-level synthesis, not a single fetched page) — corroborates `percent_complete`, `listening_status` field names against source 2; secondary to the directly-quoted docs page.
4. Web-search cross-check on unauthenticated catalog search — originally single-source and flagged unconfirmed; **superseded by the direct live fetches at sources 2A/2B in this revision**, which independently confirm the same conclusion.
5. [Audible/docs/source/auth/authentication.rst](https://github.com/mkb79/Audible/blob/master/docs/source/auth/authentication.rst) — mkb79/audible docs; sign-request vs bearer auth, token lifetimes, device-registration outputs. Fetched directly, quoted.
6. [Audible/docs/source/auth/authorization.rst](https://github.com/mkb79/Audible/blob/master/docs/source/auth/authorization.rst) and [Authorization (Login) — audible documentation](https://audible.readthedocs.io/en/latest/auth/authorization.html) — OpenID/PKCE flow, `from_login`/`from_login_external`, `/auth/register` device-registration call, init-cookie CAPTCHA note. Fetched/cross-searched.
7. [GitHub - laxamentumtech/audnexus](https://github.com/laxamentumtech/audnexus) — repo description, `/health`/`/metrics`, `MAX_REQUESTS` rate-limit config, 2025–2026 activity level, GPL v3 license. Fetched directly.
8. [Images · Product Advertising API 5.0](https://webservices.amazon.com/paapi5/documentation/images.html) — general Amazon image-URL convention (`m.media-amazon.com/images/I/...`, size modifiers); no Audible-specific PA-API program found elsewhere.
9. [Jordan Scrapes Audible Libraries Using Cookies - DEV Community](https://dev.to/aarmora/jordan-scrapes-audible-libraries-using-cookies-4419) — `x-main` cookie sufficiency for library-page title/author/cover; explicitly silent on progress data. Fetched directly, quoted.
10. [How to Scrape Audible Audiobook Data](https://crawlbase.com/blog/create-a-mini-audiobook-library-by-scraping-audible/) — client-side rendering and inconsistent cookie validity notes; practitioner blog, not Amazon-authoritative.
11. [Audnexus (audnex.us)](https://audnex.us/) — interactive API docs; base URL, version 1.8.0, endpoint list, book response fields, region list, search behavior. Fetched directly.
12. [AudiobookDB closed alpha testing · Issue #689 · laxamentumtech/audnexus](https://github.com/laxamentumtech/audnexus/issues/689) and [feat: Decouple audnexus from Audible-specific implementation · Issue #845](https://github.com/laxamentumtech/audnexus/issues/845) — maintainer's stated shift toward AudiobookDB as successor project.
13. Amazon "Request your data" / Audible privacy pages — search-summary sourced (not independently fetched from amazon.com directly); per-session listening duration, device type fields named. **Single-source, treat with caution.**
14. [How to Scrape Audible Audiobook Data](https://crawlbase.com/blog/create-a-mini-audiobook-library-by-scraping-audible/) — anti-automation posture (challenges/blocks non-browser traffic), scraping-ethics guidance (don't access login-gated personal data). Same as [10].
15. [openaudible/AudibleScraper.java](https://github.com/openaudible/openaudible/blob/master/src/main/java/org/openaudible/audible/AudibleScraper.java) — existence confirmed via search (file path only; content not independently fetched in this pass) as evidence OpenAudible mixes API and scraping.
16. [Audiobookshelf metadata providers docs](https://audiobookshelf.org/docs/documentation/community/community-providers/) and [Audiobookshelf API Reference](https://api.audiobookshelf.org/) — Audible/Audnexus used only as metadata providers; own progress tracking is internal/Socket.IO-based, not Amazon-sourced.
17. Amazon product-image URL modifier convention (`._SL500_`, `._AC_UY218_` etc.) — practitioner/community-sourced (Writing Forums, MusicBrainz Amazon Cover Art docs), not an Amazon-published spec page; corroborates but doesn't originate from source 8.
18. [Cover Art Upload Image Requirements (ACX PDF)](https://images-na.ssl-images-amazon.com/images/G/01/Audible/en_US/acx/pdf/OfficialAudibleCover-ArtRequirements.pdf) — official Audible/ACX creator-facing spec (2400×2400px minimum, true square); submission spec, not delivered-CDN-size evidence.
19. ADR-0070 (internal, not a web source): `.agentheim/knowledge/decisions/0070-steam-family-import-manual-token-only-no-login-ever.md` — read directly to align this report's risk framing and vocabulary ("login ceremony," "device registration," "paste a browser-obtained token") with the project's existing doctrine.

## Open questions

- **Exact `audible-cli library export` field list** (the "80 fields," default-column claims) was
  never independently fetched from source (GitHub raw content returned 404 in this session,
  possibly a path/branch mismatch) — should be verified with `audible library export -h` or a
  direct repo browse before relying on specific column names beyond the `response_groups` already
  confirmed via source 2.
- **Whether the `x-main`-cookie library-page scrape actually surfaces percent-complete/listening
  position anywhere in its HTML/JSON payload** — the one write-up found didn't mention it either
  way; would need a firsthand check (e.g. viewing the authenticated page's network payloads) to
  resolve, and that check itself would need to be done via the user's own browser to stay
  consistent with the ADR-0070 posture.
- **No concrete refresh-token expiry duration** is documented anywhere found — worth treating as
  "unknown, verify empirically" rather than assuming Steam-like ~24h or indefinite durability.
- **No dated, concrete incident reports of Amazon banning accounts for `mkb79/audible`-style
  unofficial API/device-registration use** were found — absence of evidence, not evidence of
  safety; a deeper pass (Audible subreddit search, Libation's GitHub issues for "banned"/"locked
  account" reports) would sharpen this if the decision hinges on it.
- **Per-marketplace host list**: the `api.audible.<tld>` convention is now confirmed live for
  `.com` and `.de` (this revision); the full list of marketplace TLDs (`co.uk`, `fr`, `ca`, `au`,
  `in`, `jp`, etc.) is still inferred from ecosystem convention rather than enumerated from a
  single authoritative source — low-risk to confirm later since Mediatheca is presumably
  US-marketplace-only.
- **Whether every `response_groups` value on `/1.0/catalog/products` is available unauthenticated**,
  or only a subset (the two live fetches in this revision used `product_desc,media,contributors`
  and `media` — both succeeded, but `price`, `sample`, and others weren't tried unauthenticated).
- **Refresh-token expiry/revocation behavior** for a `mkb79/audible`-style auth file imported from
  a user's own machine, rather than minted by Mediatheca itself, was not found in any source
  (including the newly-read `auth.py`) — still unknown/unbounded, still worth treating as
  "verify empirically" per section 5.
