---
id: integration-qqpq9
title: Audible companion PDFs — the nightly Audible sync (plus a one-time backfill) downloads the companion PDF of every library title that has one, and the book detail page links to it, opening in a new tab
status: todo
type: feature
context: integration
created: 2026-09-27
completed:
depends_on: [design-system-001, integration-cc7ab]
blocks: []
tags: [audible, books, pdf, companion-file, sync]
related_adrs: [0074, 0076, 0043, 0089]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [integration-jkbm1]
---

## Why
Some Audible audiobooks ship with a companion PDF (charts, figures, reference tables) —
e.g. *Do Not Die*. Today that PDF only exists inside Audible's own apps, so while listening
in Mediatheca's world the user has no way to get at it. Mediatheca already holds the Audible
auth file and syncs the library nightly, so it can fetch the PDF once and keep it next to the
book.

## What
- **Detect**: request Audible's `pdf_url` response group on the authenticated
  `GET /1.0/library` call `Audible.getLibrary` already makes, and decode it (plus
  `is_pdf_url_available` if present) onto `AudibleLibraryItem` as an optional companion-PDF
  URL. Titles without one decode to `None`.
- **Download**: a new adapter function in `Audible.fs` fetches the companion PDF (audible-cli's
  shape is `https://www.audible.<tld>/companion-file/<asin>`, via the existing
  `marketplaceHost`-style locale mapping), authenticating with **the route integration-cc7ab's
  ADR-0074 amendment names** (signed x-adp requests or the refresh-token→website-cookie
  exchange). A bearer token alone is proven not to work. It writes it to `<DATA_DIR>/pdfs/<asin>.pdf` (a sibling of the
  existing `images/` cache). Verify the body is a PDF (`%PDF` magic / content type) before
  keeping it; write to a temp name then rename, so a failed download never leaves a truncated
  file.
- **When**:
  - `AudibleSync.runProgressSync` (nightly job + "Sync progress now") downloads the PDF for any
    library item that has a companion-PDF URL and no file on disk yet — once per title, never
    re-downloaded while the file exists. Throttled through the existing
    `Audible.throttleMetadataCall` gate. A failed download for one item is listed in the run's
    `Errors` and retried next run; it never aborts the sync or blocks progress observations.
  - **Backfill**: because the progress sync walks the whole library every night, the first run
    after this ships naturally backfills PDFs for books already in the library — no separate
    button needed. (The one-time "Import library" path should download too, so a fresh install
    gets them on its first run.)
  - `AudibleProgressSyncResult` gains a `PdfsDownloaded: int` count, surfaced in the Settings
    card's sync result line next to the existing counts.
- **Record**: the PDF's presence is third-party, re-fetchable content — a **cache fact, not an
  event** (ADR-0043). Record it where the read side can see it (e.g. a nullable
  `companion_pdf_path` column on `book_metadata_cache`, or derive it from file existence at
  query time — worker's call; state which in the Outcome).
- **Serve**: the server serves stored PDFs at a stable URL (e.g. `/pdfs/<asin>.pdf`, the same
  static-file pattern `Composition.fs` uses for `/images`) with `Content-Type: application/pdf`
  and **inline** disposition, so the browser's PDF viewer opens it.
- **Link**: the book detail page (`src/Client/Pages/BookDetail`) shows a "Companion PDF" link
  in the details card when the book has one, opening in a new tab (`target="_blank"`,
  `rel="noopener"`). No link at all for books without one. Book detail DTO gains an optional
  companion-PDF URL field.

## Acceptance criteria
- [ ] `Audible.getLibrary` decodes a library item's companion-PDF URL from a recorded fixture
      that has one (*Do Not Die*), and decodes `None` for an item without one.
- [ ] Running the progress sync against a stubbed Audible with one PDF-bearing item writes
      `<DATA_DIR>/pdfs/<asin>.pdf` and reports `PdfsDownloaded = 1`; a second run with the file
      present makes no PDF request and reports `PdfsDownloaded = 0`.
- [ ] A non-PDF or failed download response leaves no file on disk, lists the item in
      `Errors`, and the same run's progress observations are still issued.
- [ ] `GET /pdfs/<asin>.pdf` for a stored file returns 200, `Content-Type: application/pdf`, and
      no `attachment` disposition; an unknown asin returns 404.
- [ ] Book detail DTO for a book with a stored PDF carries its URL; for a book without one it
      carries `None`.
- [ ] The book detail page renders a "Companion PDF" link with `target="_blank"` only when the
      DTO field is `Some` (client unit test on the view helper, or an e2e check on the isolated
      5100 stack).
- [ ] No event type is added or changed — PDF presence lives only in cache/files.
- [ ] The link sits naturally in the details card, styled per the StyleGuide
      (`DesignSystem.fs`), not as a bespoke one-off. [human-eye]

## Notes
- **Wire shape is unverified** — this is the main risk. `pdf_url` as a library response group
  and the `companion-file/<asin>` endpoint come from audible-cli (`LibraryItem.get_pdf_url`),
  not from a recording against this account. The worker must record a real fixture of the
  library item and the PDF download for *Do Not Die* (fixtures only — **never touch the live
  DB**; the builder supplies an auth file or recorded responses if needed, as with
  integration-jkbm1's iteration 2).
- **Auth route (refined 2026-09-27)**: the first attempt bounced (see Worker note). Bearer-only
  auth can't download the PDF. This task now waits on the spike **integration-cc7ab**, which
  tests signed x-adp requests and then the refresh-token→cookie exchange, and reports an
  ADR-0074 amendment for whichever works. Implement exactly that route, reusing the spike's
  recorded fixture. If the spike finds **no** working route, re-refine this task into a
  link-out: decode `pdf_url` and link to `audible.<tld>/companion-file/<asin>` in a new tab,
  with no download, no `pdfs/` folder and no `/pdfs` route.
- **Auth route settled (2026-09-27, ADR-0089)**: the spike shipped. Use **adp-signed requests**:
  `x-adp-token` / `x-adp-alg: SHA256withRSA:1.0` / `x-adp-signature` headers, built from the auth
  file's `AdpToken` + `DevicePrivateKey`, with no bearer and no `client-id`, on
  `GET www.audible.<tld>/companion-file/<asin>`. **Follow the 302** to the signed CloudFront URL
  to get the bytes. The cookie exchange is **not** adopted. The body arrives as
  `application/octet-stream`, so check the `%PDF` magic bytes, not the content type. Reuse the
  redacted fixture `tests/Server.Tests/fixtures/audible/companion-pdf-signed-request.json`.
  The link-out fallback above no longer applies.
- Consider whether `pdfs/` should be covered by whatever backup/export treats `images/`
  (Administration) — mention it in the Outcome if not handled.
- Docker: `pdfs/` lives under `DATA_DIR`, so it's already on the mounted volume on harbour.

## Worker note

Recorded live evidence (via the supplied auth file, marketplace `de`, bearer access token minted through `Audible.refreshAccessToken`'s own `/auth/token` flow) shows the companion-PDF download is not reachable with ADR-0074's bearer-only auth, matching the task's own named stop condition. `GET /1.0/library?response_groups=product_desc,pdf_url` decodes fine and returns items carrying a `pdf_url` (e.g. a real title's `pdf_url` pointed at `https://d2fahduf2624mg.cloudfront.net/post_purchase_docs/.../*.pdf`, `is_pdf_url_available` was `null` on every item seen), so the detect/decode half of this task is low-risk. But both plausible download paths refuse the bearer token: (1) `GET https://www.audible.de/companion-file/<asin>` with `Authorization: Bearer <token>` returns HTTP 302 redirecting to `www.amazon.de/ap/signin` (Amazon's login page) -- byte-identical behaviour to sending the exact same request with no Authorization header at all, i.e. the endpoint ignores the bearer token entirely and demands a real logged-in browser session; (2) fetching the `pdf_url` CloudFront link directly returns HTTP 403 with a CloudFront `AccessDenied` XML body -- both unauthenticated and with the auth file's own stored `website_cookies` attached (5 cookies present in the file; still 403), confirming the link needs CloudFront *signed* cookies minted by a live, successful web-session hit of the companion-file page, not the static cookies quickstart happened to capture. This is exactly the task's Notes/spawn-prompt stop clause ("if the companion-file endpoint refuses a bearer token -- e.g. it needs signed x-adp-* requests or website cookies -- stop and bounce... adding request signing/cookie-session auth is a decision that amends ADR-0074, not something to slip in"). Implementing a fix (a login-derived, refreshed CloudFront-signed-cookie session, or x-adp request signing) is a decision this worker is not authorized to make. No repo files were written; no fixtures, tokens, or cookies were committed (verified via `git status`/`git diff --stat` in the worktree, both empty).
