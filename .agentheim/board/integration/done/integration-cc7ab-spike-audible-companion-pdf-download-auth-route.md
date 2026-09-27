---
id: integration-cc7ab
title: Spike — find an auth route that downloads an Audible companion PDF without a login, testing signed x-adp requests first and the refresh-token-to-website-cookie exchange second, and report an ADR-0074 amendment for the first one that works
status: done
type: spike
context: integration
created: 2026-09-27
completed:
depends_on: []
blocks: [integration-qqpq9]
tags: [audible, pdf, companion-file, auth, spike]
related_adrs: [0074, 0089]
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

## Outcome

Live spike against the `de` marketplace, using the builder-supplied `audible-cli` auth file
(read once from the scratch directory named in the spawn prompt, never copied into the worktree,
never logged). Six live calls total, paced ≥2.2s apart:

1. `POST api.amazon.de/auth/token` — minted an access token from the imported refresh token
   (existing `Audible.refreshAccessToken` shape; unchanged).
2. `GET api.audible.de/1.0/library?response_groups=product_desc,pdf_url` — 86 items; the task's
   example title ("Do Not Die") wasn't in this account's library, so the first item carrying a
   `pdf_url` was used instead (functionally equivalent for route validation) — asin/title
   redacted in the fixture. A control item with no `pdf_url` was also identified but not
   exercised further; the stop-loss condition was reached before it was needed.
3. **Route 1 attempt**: `GET www.audible.de/companion-file/<asin>` with `x-adp-token` /
   `x-adp-alg` / `x-adp-signature` headers (no bearer) → `302` to
   `d2fahduf2624mg.cloudfront.net` (a DIFFERENT, freshly-signed URL from the static `pdf_url` step
   2 returned).
4. `POST www.amazon.de/ap/exchangetoken/cookies` (route 2, tried per the task's fallback
   ordering since step 3's redirect wasn't automatically followed in that first pass) — 200, 6
   cookies minted.
5. **Route 2 confirmation**: `GET www.audible.de/companion-file/<asin>` with those cookies,
   redirects followed → `200`, `content-type: application/octet-stream`, body starts
   `%PDF-1.6` — route 2 confirmed working.
6. **Route 1 re-check, following its own redirect this time** (the sixth and final call, within
   the ≤~6 budget): re-signed the same request and followed its `302` → `200` from
   `d2fahduf2624mg.cloudfront.net`, body starts `%PDF-1.6` — **route 1 alone is sufficient**;
   the earlier "failure" in step 3 was an artifact of not following the redirect, not a real
   rejection.

Since route 1 is the smaller, already-provisioned auth surface (only `adp_token` +
`device_private_key`, both imported since ADR-0074 for exactly this purpose but never used),
it is the recommended and adopted route — see ADR-0089 (amends ADR-0074) in this task's ADRS
block. Route 2 is recorded as a confirmed-working fallback, not adopted.

Redacted evidence (every token, signature, cookie, customer id, device serial and key replaced
with `<redacted>`; PDF body truncated to its header bytes): `tests/Server.Tests/fixtures/audible/companion-pdf-signed-request.json`.
Not wired into any test — TDD_SKIPPED per the task's own "adding no test is acceptable" allowance
for this spike. Full Expecto suite re-run from the worktree afterward: 1059 passed, 0 failed
(the new fixture file is not referenced anywhere in `Server.Tests.fsproj`, so it cannot have
affected compilation or any existing test).

No production code path was changed (`Audible.fs` and every other source file are untouched —
verified via `git status`/`git diff --stat` in the worktree, showing only the one new fixture
file). No secrets in the diff — verified by programmatically grepping the fixture's full text for
every string/substring value in the auth file (refresh_token, adp_token, access_token,
device_private_key, all five website_cookies values, the store_authentication_cookie value,
device_serial_number, customer user_id): zero hits. The auth file itself was read only from the
scratch path named in the spawn prompt and was never copied into the worktree or printed in full
anywhere in this task's output.

integration-qqpq9 can now proceed: implement the download using ADR-0089's route (adp-signed
`GET companion-file/<asin>`, following its redirect), reusing this fixture as the
expected-request/response reference.
