---
id: 0089
title: Audible companion-PDF download uses an adp-signed request, following its redirect — no cookie exchange, no login (amends ADR-0074)
scope: integration
status: accepted
date: 2026-09-27
related_tasks: [integration-cc7ab, integration-qqpq9]
related_adrs: [0074]
amends: [0074]
---

# ADR-0089: Audible companion-PDF download uses an adp-signed request (amends ADR-0074)

## Context

integration-qqpq9 (Audible companion PDFs) bounced: with ADR-0074's bearer-only adapter, the
companion-file endpoint (`GET www.audible.<tld>/companion-file/<asin>`) redirects a bearer-token
request to `www.amazon.<tld>/ap/signin` (identical behaviour to no auth at all), and the raw
`pdf_url` a library item carries (a `d2fahduf2624mg.cloudfront.net/post_purchase_docs/...pdf`
link) 403s `AccessDenied` whether unauthenticated or with the auth file's static
`website_cookies` attached.

This spike (integration-cc7ab) tried the two routes ADR-0074 left untested, live, against the
`de` marketplace, using the builder's own `audible-cli` auth file, in the task's stated order:

1. **Signed request** (`mkb79/audible`'s `Authenticator.sign_request` shape): `x-adp-token`,
   `x-adp-alg: SHA256withRSA:1.0`, `x-adp-signature: <base64 signature>:<date>` headers, where
   the signature is an RSA-SHA256 signature (over the auth file's PKCS1 `device_private_key`) of
   the string `{method}\n{path}\n{date}\n{body}\n{adp_token}` — no `Authorization`/bearer header
   at all.
2. **Cookie exchange** (`mkb79/audible`'s `refresh_website_cookies` / the same pattern `alexapy`
   uses for Alexa): `POST https://www.amazon.<tld>/ap/exchangetoken/cookies` with the refresh
   token as `source_token`, `requested_token_type=auth_cookies`, minting website cookies, then
   `GET companion-file/<asin>` with those cookies attached.

**Both routes reached a real PDF body live** (`%PDF-1.6` magic bytes, `content-type:
application/octet-stream`, served from `d2fahduf2624mg.cloudfront.net`). The decisive
observation for route 1: its `GET companion-file/<asin>` response is a `302` whose `Location`
header points at the SAME CloudFront host as the library's static `pdf_url` — but, unlike that
static link, the signed request's redirect target carries a valid, live-minted signature in its
query string, and following it (ordinary HTTP redirect-following, no second signed request
needed) returns the PDF directly. Route 2 also worked when tried as a fallback confirmation, but
needs an extra live Amazon-side cookie mint the adapter does not otherwise need.

Full redacted request/response sequence for both routes: `tests/Server.Tests/fixtures/audible/companion-pdf-signed-request.json`.

## Decision

1. **The companion-PDF download route is adp-signed requests, route 1 above.** `Audible.fs`
   gains a signing function that computes the `x-adp-token` / `x-adp-alg` / `x-adp-signature`
   headers from the auth file's existing `AdpToken` and `DevicePrivateKey` fields (imported since
   ADR-0074 "for signed requests" but unused by the adapter until now) and issues
   `GET www.audible.<tld>/companion-file/<asin>` with those headers alone (no bearer, no
   `client-id`). The caller MUST follow the resulting redirect (an ordinary HTTP client
   behaviour) to reach the PDF bytes — the endpoint never returns the PDF body directly.
2. **The website-cookie exchange (route 2) is NOT adopted**, even though it also works live: it
   requires an extra Amazon-side call that mints website cookies the adapter has no other use
   for, widening the auth surface for no benefit once route 1 is confirmed sufficient. Not
   forbidden outright — a future spike may revisit it if route 1 ever regresses — but it is not
   part of the download path this ADR authorizes.
3. **Still no login, no `/auth/register`, no device registration.** The signing function reads
   only fields already present in the imported auth file; it mints nothing new and cannot recover
   from a bad `adp_token`/`device_private_key` any more than `refreshAccessToken` can recover
   from a bad refresh token.
4. **A rejection of the signed request (a redirect to `amazon.<tld>/ap/signin` instead of the
   CloudFront host, or any non-2xx/3xx after following the redirect) is surfaced through the SAME
   `Audible.authFileRejectedPrefix` ("audible auth file rejected: ") path ADR-0074 point 4
   already defines** — driving the same standing Settings notice ("paste a fresh auth file"),
   never a login retry, never a second signing attempt with different parameters.
5. **integration-qqpq9 implements the download using exactly this route**, reusing the recorded
   fixture as its stub/expected-shape reference.

## Alternatives considered

- **Cookie exchange (route 2)** — confirmed to work live, but requires minting website cookies
  the adapter otherwise never touches; a wider, less-necessary surface once route 1 is proven
  sufficient. Not adopted; recorded as a fallback should route 1 ever stop working.
- **No working route; link out instead** — moot: both tested routes worked live. This ADR exists
  because a route was found, not despite failing to find one.

## Consequences

- `Audible.fs` gains one new signing helper (pure over the request shape, testable with a fixed
  date/key/token) plus the actual `GET`-and-follow-redirect call; no other adapter function
  changes.
- The companion-PDF download now depends on `AdpToken`/`DevicePrivateKey` actually being usable
  for signing, not merely present-and-decodable as `validateAuthFile` already checks — the same
  "shape validated, live behaviour confirmed separately" split ADR-0074's own refresh-token
  ceremony already has.
- No change to `refreshAccessToken`, `withAccessToken`, `getLibrary`, or any bearer-authenticated
  path — this is additive, scoped to the one new endpoint.
