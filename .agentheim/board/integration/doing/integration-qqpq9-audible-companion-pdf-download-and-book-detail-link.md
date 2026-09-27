---
id: integration-qqpq9
title: Audible companion PDFs — the nightly Audible sync (plus a one-time backfill) downloads the companion PDF of every library title that has one, and the book detail page links to it, opening in a new tab
status: doing
type: feature
context: integration
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [audible, books, pdf, companion-file, sync]
related_adrs: [0074, 0076, 0043]
related_research: []
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
  `marketplaceHost`-style locale mapping) using the same `withAccessToken` bearer token the
  library call already minted, and writes it to `<DATA_DIR>/pdfs/<asin>.pdf` (a sibling of the
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
- **Auth risk**: ADR-0074 made the adapter bearer-only. If the companion-file endpoint refuses a
  bearer token (e.g. it needs signed `x-adp-*` requests or website cookies), **stop and bounce
  back to backlog** with the recorded response — adding request signing is a decision that
  amends ADR-0074, not something to slip in.
- Consider whether `pdfs/` should be covered by whatever backup/export treats `images/`
  (Administration) — mention it in the Outcome if not handled.
- Docker: `pdfs/` lives under `DATA_DIR`, so it's already on the mounted volume on harbour.
