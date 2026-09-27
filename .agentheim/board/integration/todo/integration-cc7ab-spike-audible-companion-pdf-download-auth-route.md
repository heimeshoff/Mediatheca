---
id: integration-cc7ab
title: Spike — find an auth route that downloads an Audible companion PDF without a login, testing signed x-adp requests first and the refresh-token-to-website-cookie exchange second, and report an ADR-0074 amendment for the first one that works
status: todo
type: spike
context: integration
created: 2026-09-27
completed:
depends_on: []
blocks: [integration-qqpq9]
tags: [audible, pdf, companion-file, auth, spike]
related_adrs: [0074]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
prior_art: []
---

## Why
integration-qqpq9 (Audible companion PDFs) was bounced: with ADR-0074's bearer-only
adapter, the PDF can't be downloaded. Live evidence (marketplace `de`, access token minted
through `Audible.refreshAccessToken`):
- `GET /1.0/library?response_groups=product_desc,pdf_url` works and returns `pdf_url`
  (a `d2fahduf2624mg.cloudfront.net/post_purchase_docs/...pdf` link; `is_pdf_url_available`
  was `null` everywhere).
- `GET https://www.audible.de/companion-file/<asin>` with `Authorization: Bearer` returns
  302 to `www.amazon.de/ap/signin`. That's the same as sending no auth at all.
- The CloudFront `pdf_url` returns 403 `AccessDenied`, whether unauthenticated or with the
  auth file's stored `website_cookies`.

Two routes are still untested, and neither is a login or a device registration (both use only
the user-registered device in the imported auth file):
1. **Signed requests.** ADR-0074 already imports `adp_token` and `device_private_key` "for
   signed requests". The `mkb79/audible` Authenticator signs every request it sends
   (`x-adp-token`, `x-adp-alg`, `x-adp-signature`), and audible-cli downloads PDFs through that
   client. The earlier attempt only sent a bearer token.
2. **Cookie exchange.** `mkb79/audible`'s `refresh_website_cookies` exchanges the refresh
   token for fresh website cookies via `POST https://www.amazon.<tld>/ap/exchangetoken/cookies`.
   With those cookies, `companion-file/<asin>` may redirect to a CloudFront-signed URL.

## What
Try the routes in order against *Do Not Die* (and one title without a PDF as a control),
using an auth file the builder supplies:
1. Signed request: `GET www.audible.<tld>/companion-file/<asin>` (and `pdf_url` directly)
   with the adp request signature, as `mkb79/audible` computes it
   (`method\npath\ndate\nbody\nadp_token`, SHA256withRSA over the device private key).
2. Only if (1) fails: cookie exchange via `/ap/exchangetoken/cookies`, then
   `companion-file/<asin>` with the minted cookies, following redirects.

**Stop-loss:** if, mid-spike, the mitigation is already known and cheap, record it and stop.
The first route that returns a real PDF body (`%PDF` magic) ends the spike; don't go on to
test the other one.

## Acceptance criteria
- [ ] Each attempted route has its request shape (headers named, secrets redacted) and response
      (status, redirect `Location` host, content type, first bytes) recorded in the Outcome.
- [ ] A working route is recorded as a redacted fixture of the successful request/response
      sequence under the server test fixtures, and the auth file, tokens and cookies are not in
      the diff.
- [ ] If a route works, the Outcome reports an ADR (amending ADR-0074) that names exactly what
      the adapter is newly allowed to do (request signing and/or the cookie exchange). The ADR
      must restate that there is still no login, no `/auth/register`, and that a rejection means
      "paste a fresh auth file".
- [ ] If neither route works, the Outcome says so, with the evidence, and recommends dropping the
      download in favour of linking out to Audible's companion-file page.
- [ ] No production code path is changed. This spike ships evidence, fixtures and a decision only.

## Notes
- **Never touch the live database.** The only live contact allowed is with Amazon/Audible,
  using the builder-supplied auth file. Don't commit it.
- Keep the calls to a handful and stay within `Audible.throttleMetadataCall`-style pacing.
  This is the builder's only Amazon account.
- Reference implementations: `mkb79/audible` (`audible/auth.py`: `sign_request`,
  `refresh_website_cookies`) and `mkb79/audible-cli` (`LibraryItem.get_pdf_url`, `download --pdf`).
- The ADR decides; integration-qqpq9 then implements the download using the route it names.
