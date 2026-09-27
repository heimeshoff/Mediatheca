---
id: integration-t4q7k
title: Wire companion-PDF download into the one-time "Import library" bootstrap path
status: backlog
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

integration-qqpq9 wires the companion-PDF download into `AudibleSync.runProgressSync` (the nightly
job + Settings' "Sync progress now") only — ADR-0090 records why the one-time "Import library"
bootstrap (`Api.importAudibleLibraryImpl`) was deliberately left untouched: doing so would require
adding a `pdfBasePath` parameter (or equivalent seam) to `Api.create`, whose signature is
constructed positionally at roughly 45 call sites across ~20 test files plus `Composition.fs`. A
fresh install's very first "Import library" click therefore does NOT download companion PDFs yet —
they arrive on the next nightly run or the next manual "Sync progress now" click, both of which
already walk the whole library and would download them regardless.

## What

- Thread a way for `Api.importAudibleLibraryImpl` to reach `AudibleSync.downloadCompanionPdfIfMissing`
  (already implemented and tested, integration-qqpq9) — either a `pdfBasePath: string` parameter on
  `Api.create` (mirroring `imageBasePath`), or a narrower partially-applied callback (mirroring how
  `createBook`/`imageBasePath` are already threaded into `AudibleSync.runProgressSync` from
  `Composition.fs`) — evaluate both for the smaller diff.
- Call the download helper from `importAudibleLibraryImpl`'s per-item loop (`observe`/the item
  for-loop in `src/Server/Api.fs`), the same way the nightly sync's loop does: before or independent
  of the progress/prior recording, never blocking or aborting the import on a single item's PDF
  failure.
- Update every `Api.create` call site the chosen seam touches (`grep -rn "Api\.create" tests/
  src/Server/Composition.fs`).
- Consider whether `AudibleImportResult` should gain its own `PdfsDownloaded: int` count, mirroring
  `AudibleProgressSyncResult`'s field, if the chosen seam makes that natural.

## Acceptance criteria

- [ ] Running "Import library" against a stubbed Audible library containing one PDF-bearing,
      previously-unknown item downloads its companion PDF and writes `<DATA_DIR>/pdfs/<asin>.pdf`.
- [ ] A book already carrying a companion-PDF file on disk is never re-downloaded by
      "Import library".
- [ ] A failed companion-PDF download during import never aborts the import, and that item's
      progress/prior recording still happens.
- [ ] Every existing `Api.create` test call site still compiles and passes.

## Notes

Reuse `AudibleSync.downloadCompanionPdfIfMissing`/`PdfStore`/`MetadataCache.setBookCompanionPdfPath`
verbatim — the exact functions integration-qqpq9 already wrote and tested. This task is pure wiring,
not new adapter logic.