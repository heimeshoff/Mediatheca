---
id: 0074
title: Audible credentials are imported from an auth file the user generates on their own machine with audible-cli; Mediatheca never performs the Amazon login or device registration, and a rejected refresh token means "paste a fresh auth file", never re-register
scope: integration
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
amends: []
related_tasks: [integration-dhctm, integration-jjvg2]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
---

# ADR 0074: Audible credentials are imported from a user-run `audible-cli` auth file — Mediatheca never registers a device

## Context

The builder wants Audible to serve books the way TMDB serves movies and Steam serves games: catalog
search in the search modal, a details page, and **listening progress** (`percent_complete`) per
title, refreshed on a cadence. Research (`audible-api-surface-and-listening-progress-2026-09-16`)
established:

- There is **no official Audible API**. Everything usable is the unofficial internal API
  (`api.audible.<tld>/1.0/…`) that the `mkb79/audible` library, `audible-cli`, Libation and
  OpenAudible all use.
- **Catalog search and product detail work without credentials** (verified live by the review
  gate: `GET /1.0/catalog/products?keywords=…&response_groups=product_desc,media,contributors`
  returns full results unauthenticated).
- **The library and listening status (`GET /1.0/library` with `percent_complete`, `is_finished`,
  `listening_status`) require an authenticated, device-registered client.** The only way to obtain
  the durable refresh token is Amazon's OAuth Authorization-Code + PKCE login **followed by a POST
  to `/auth/register`**, which mints a new device identity (refresh token, RSA private key,
  `adp_token`) on the Amazon account.
- An access token lives ~60 minutes; a stored refresh token mints new access tokens via Amazon's
  token endpoint without any further login or registration. The refresh token's own lifetime is
  undocumented.

ADR-0070 rules that Mediatheca **never performs a Steam login or mints a Steam token** after
Valve's permanent-ban threat traced to a login ceremony run from the Docker host. That decision is
Steam-scoped, but its reasoning is general: a third-party login/registration ceremony executed
by the server, from the server's IP, on the builder's only account, is an act the builder cannot
afford to have flagged. An Amazon device registration run inside Mediatheca is the same category of
act. The research found no dated reports of Amazon locking accounts over `mkb79/audible`-style
use (Libation has done this for years), so the risk is milder than Steam's — but "milder" is not
"absent", and the builder has been through this once already.

There is, however, a materially different option available for Audible that Steam never had: the
registration ceremony can run **entirely on the user's own machine, in the user's own hands**,
via `audible-cli quickstart`, producing an auth-file JSON. That file carries everything the server
needs to call `/1.0/library` on the user's behalf **without ever registering anything itself**:
the refresh token (to mint access tokens), the `adp_token` + `device_private_key` (for signed
requests), the marketplace/locale, and the customer info.

## Decision

1. **Mediatheca never performs an Amazon/Audible login and never calls `/auth/register`.** No
   username/password field, no QR or external-browser login flow, no device profile, no SteamKit-
   style satellite. The `Audible.fs` adapter has no code path that can register a device.

2. **The only Audible credential is an imported auth file.** Settings → Audible offers a textarea
   where the user pastes the JSON that `audible-cli quickstart` (run on their own machine, with
   the registration happening there) writes to its auth file. The server validates the shape
   (`refresh_token`, `adp_token`, `device_private_key`, `locale_code`, `customer_info` present),
   persists it under `audible_auth_file` in `SettingsStore`, and derives the marketplace host from
   `locale_code`. The Settings page explains the one-time local procedure in three lines.

3. **Minting an access token from the imported refresh token is permitted.** It is a token
   refresh on a device the *user* registered — the same shape as the Jellyfin re-auth seam
   (ADR-0011) and categorically different from the Steam refresh-token seam ADR-0070 deleted,
   which existed only because the server had performed the login that produced it. The adapter
   caches the minted access token and its expiry in `SettingsStore` (`audible_access_token`,
   `audible_access_token_expires`) and refreshes on expiry or on a 401.

4. **A rejected refresh token is surfaced as data, never repaired by a login.** A 401/403 from the
   token endpoint or from `/1.0/library` after one refresh attempt persists
   `audible_last_error` with a fixed `"audible auth file rejected: "`-prefixed message, drives a
   standing Settings notice ("paste a fresh auth file from `audible-cli`"), and stops the sync job
   for that run. Nothing retries a login, because there is no login to retry.

5. **Catalog search and product detail run unauthenticated** against the marketplace host
   derived from the auth file's locale (falling back to an `audible_marketplace` setting, default
   `de`, when no auth file is present) — so the search modal's Audible source works before any
   credential is pasted. Audnexus (`api.audnex.us/books/{asin}`) is the metadata fallback for
   description, narrators and series when the product response is thin.

6. **The auth file is treated as a secret.** `getAudibleAuthStatus` returns customer name,
   marketplace and the token's expiry — never the file. The file is never logged, never exported
   with the event log (it lives in `settings`, an Imperative table, not in events).

## Alternatives considered

- **Server-side `from_login_external` (user logs in in their own browser, pastes the redirect URL
  back)** — still performs `/auth/register` from the server. Rejected: the registration is the act
  ADR-0070's reasoning forbids, regardless of where the browser half runs.
- **Manual `audible-cli library export` file upload with `percent_complete`** — doctrine-clean but
  a human-run refresh, so "progress on a cadence" degrades to "progress when the user remembers".
  Kept as a documented fallback the sync task may add later; not the primary path.
- **Scraping the authenticated web library page with a pasted `x-main` cookie** — unconfirmed to
  expose progress at all, and scraping-fragile. Rejected.
- **No Audible progress; manual entry only** — fails the builder's explicit goal.

## Consequences

- The builder runs `pipx install audible-cli && audible quickstart` once, on their own machine,
  and pastes the resulting `~/.audible/<profile>.json` into Settings. That is the only ceremony.
- If Amazon ever invalidates the device (refresh token rejected), the sync stops and the Settings
  notice asks for a fresh file — the user decides whether to re-run `quickstart`; Mediatheca never
  decides that for them.
- The unauthenticated catalog search means the Audible search source has no "not configured"
  state; only the library import and the progress sync are gated on the auth file.
- `SteamFamilyTokenRejected`'s wording-distinct rejection pattern (ADR-0065/0070) is reused:
  `"audible auth file rejected: …"` never shares a prefix with any other adapter's failure.
