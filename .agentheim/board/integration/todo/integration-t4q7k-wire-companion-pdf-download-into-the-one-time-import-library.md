---
id: integration-t4q7k
title: Wire companion-PDF download into the one-time "Import library" bootstrap path
status: todo
type: feature
context: integration
created: 2026-09-27
completed:
depends_on: [integration-qqpq9]
blocks: []
tags: [audible, books, pdf, companion-file]
related_adrs: [0090, 0089, 0043]
related_research: []
prior_art: [integration-qqpq9]
---

## Why

integration-qqpq9 wired the companion-PDF download into `AudibleSync.runProgressSync` only (the
nightly job and Settings' "Sync progress now"). ADR-0090 left the one-time "Import library"
bootstrap (`Api.importAudibleLibraryImpl`) alone, because reaching it means threading a new
parameter through `Api.create`, which is built positionally at every call site. So on a fresh
install the first "Import library" click downloads no companion PDFs. They only show up after the
next nightly run or a manual "Sync progress now". The live instance has already imported, so this
closes the gap for fresh installs and for any rebuild from an empty data dir.

## What

**Seam (decided 2026-09-27, builder's choice; ADR-0090 amendment):** an explicit
`(pdfBasePath: string)` parameter on `Api.create`, placed directly after `imageBasePath`. It
mirrors how `imageBasePath` is threaded and keeps the explicit, test-isolatable configuration rule
from ADR-0090's "Alternatives considered". Rejected options: chaining `runAudibleProgressSyncNow`
after the import (a second library walk, plus a second round of observations right after the
priors) and deriving the path from `imageBasePath`'s parent directory (hidden coupling, and
`pdfs/` folders collide between tests that share a temp directory).

1. **`Api.create`** gains `(pdfBasePath: string)` after `(imageBasePath: string)`. Its
   `importAudibleLibrary` field passes it through to `importAudibleLibraryImpl`, which also gains
   `(pdfBasePath: string)` after `imageBasePath`.
2. **`Composition.fs`**: the `Api.create` call (currently line ~755) passes the existing
   `pdfBasePath` value (`Path.Combine(dataDir, "pdfs")`, line ~342). That value must be bound
   before the call, which it already is.
3. **Per-item download inside `importAudibleLibraryImpl`**: at the top of the `observe slug item`
   closure, which all three slug-bearing branches of the `for item in items` loop already call
   (matched, created, duplicate found), run exactly what the nightly sync does:
   `AudibleSync.downloadCompanionPdfIfMissing httpClient (Audible.webHost authFile.LocaleCode)
   authFile pdfBasePath item`.
   - `Ok None` → nothing to do.
   - `Ok (Some bytes)` → `PdfStore.save pdfBasePath item.Asin bytes`, then
     `MetadataCache.setBookCompanionPdfPath conn slug (PdfStore.relativePath item.Asin)` (no lock:
     the import owns its connection, `noLocker`), then increment `pdfsDownloaded`.
   - `Error e` → append `"<title> (<asin>): companion PDF: <e>"` to `errors` and carry on to the
     progress/prior recording. Never abort the item or the import.
4. **`AudibleImportResult`** (`src/Shared/Shared.fs`) gains `PdfsDownloaded: int`, the same field
   `AudibleProgressSyncResult` has. Fill it from the counter.
5. **Settings import summary** (`src/Client/Pages/Settings/Views.fs`, the `Imported: …` alert
   text): append `, %d PDFs downloaded`, in the same style as the existing
   `Synced: … %d PDFs downloaded` line below it. Update the `AudibleImportResult` literal in
   `src/Client/Pages/Settings/AudibleImportSync.test.fs` for the new field. This only extends the
   text of an existing alert and adds no new component, so the styleguide gate doesn't apply.
6. **Every `Api.create` call site in `tests/`** (31 today across ~20 files: `grep -rn
   "Api\.create\b" tests | grep -v createBook`) gets the new argument. Use a fresh per-test temp
   directory, or the directory the test already uses for images with a `pdfs` subfolder. Never
   pass a shared or global path.

## Acceptance criteria

- [ ] An Expecto test runs "Import library" against a stubbed Audible library with one
      previously unknown item that carries a `pdf_url`. The companion PDF is written to
      `<pdfBasePath>/<asin>.pdf`, the book's metadata-cache companion-PDF path is set to
      `PdfStore.relativePath asin`, and the result reports `PdfsDownloaded = 1`.
- [ ] An Expecto test: a library item whose `<pdfBasePath>/<asin>.pdf` already exists makes no
      PDF HTTP request during "Import library" and reports `PdfsDownloaded = 0`.
- [ ] An Expecto test: when the stubbed PDF download fails (for example a 500), the import still
      returns `Ok`, `Errors` contains that item's `companion PDF:` entry, and that book's
      progress/prior is still recorded (`ProgressObserved` is unchanged from the no-PDF case).
- [ ] A matched item (the book already exists and is found by ASIN) also gets its companion PDF
      downloaded during import, just like a newly created one.
- [ ] `AudibleImportResult` has `PdfsDownloaded: int`, and the Settings "Imported: …" summary
      text includes the PDF count.
- [ ] `npm run build`, `npm test` (Expecto) and `npm run test:client` (Vitest) all pass. Every
      existing `Api.create` call site compiles with the new argument.

## Notes

- This is wiring only. Reuse `AudibleSync.downloadCompanionPdfIfMissing`, `PdfStore` and
  `MetadataCache.setBookCompanionPdfPath` as integration-qqpq9 left them. Don't change the helper.
  The nightly sync's per-item block in `src/Server/AudibleSync.fs` (~line 311) is the pattern to
  copy.
- The import is one-time (integration-dvbjp): it refuses to run after its first populated run.
  Every new test needs a fresh in-memory DB with no `audible_library_imported_at` stamp.
- `Api.create` changes arity. Per the project memory "Run the full suite on main after a
  parallel batch", don't batch this task in parallel with another task that also touches
  `Api.create`'s parameter list, or run the full suite on main right after integrating.
- ADR-0090 gained an amendment on 2026-09-27 recording that this task reverses its decision §2
  with the seam described above.
