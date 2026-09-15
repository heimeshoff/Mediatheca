---
id: books-g7g1j
title: Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route
status: todo
type: feature
context: books
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, integration-dhctm, design-system-001-formalize-styleguide]
blocks: []
tags: [books, search-modal, frontend, open-library, audible]
related_adrs: [0075, 0074, 0016]
related_research: []
prior_art: []
---

## Why

"Can search them in the search modal." The modal is the one way into the library for every media
type; books must be addable from it exactly like movies (TMDB) and games (RAWG/Steam), with the
games-k3vps source toggles as the template.

## What

`src/Client/Components/SearchModal.fs` + root `src/Client/State.fs` (dispatch lives there):
- `SearchTab` gains `Books`; the tab strip shows "Books" after "Games".
- Two session-local source checkboxes on the Books tab: **Open Library** (checked by default) and
  **Audible** (checked by default — it needs no credential; unchecked state persists only for the
  modal's lifetime, like games-k3vps). `Model` gains `OpenLibraryResults`, `AudibleResults`,
  `IsSearchingOpenLibrary`, `IsSearchingAudible`, `IncludeOpenLibrary`, `IncludeAudible`.
- `Query_changed` on the Books tab debounces 300 ms then fires `api.searchOpenLibraryBooks` and/or
  `api.searchAudibleBooks` per toggle (the `gamesSearchCmds` shape), guarded by `SearchVersion`.
- Results render in the merged poster grid as portrait cover cards (`PosterCard`) with a source
  badge ("Open Library" / "Audible"), title, authors, year, and for Audible the narrator + runtime
  line. No hover preview in this task (Books has no preview endpoint yet).
- `Import_openlibrary of OpenLibrarySearchResult` → `api.addBookFromOpenLibrary { WorkKey;
  EditionKey; SkipDuplicateCheck = false }`; `Import_audible of AudibleSearchResult` →
  `api.addBookFromAudible { Asin; SkipDuplicateCheck = false }`. `Duplicate_found` reuses the existing
  `DuplicatePrompt` ("open existing" / "add anyway" / cancel) — extend `PendingGameImport` into a
  general `PendingImport` DU (or add `PendingBookImport`) so "add anyway" re-sends with
  `SkipDuplicateCheck = true`. `Import_completed (Ok (slug, MediaType.Book))` navigates to
  `Book_detail slug`.
- Library tab: `loadSearchLibraryCmd` also fetches `api.getBooks`; `filterLibrary` includes books
  (`MediaType.Book`, cover as poster); `Navigate_to (slug, Book)` → `/books/{slug}`.
- `Router.fs`: `Book_detail of slug` case, `["books"; slug]` parse, `toUrl`, and `isDashboardSection`
  including it (the detail page itself is books-f33e2 — until it lands, the route may render
  `NotFound`; this task adds the route so navigation compiles, and books-f33e2 fills the page).
  If books-f33e2 merges first, do nothing here.

## Acceptance criteria

- [ ] `SearchModal.test.fs` (Fable.Mocha): `filterLibrary` returns a book by fuzzy title with
      `MediaType = Book`; the Books-tab reducer seam (extract `applyBooksSearchResults`) merges Open
      Library and Audible results and drops stale results by `SearchVersion`; toggling Audible off
      clears `AudibleResults` and skips the Audible command (assert on the returned `Cmd` list being
      empty for that source, or on a pure `booksSearchPlan` function returning `[OpenLibrary]`).
- [ ] `Route.test.fs`: `parseUrl ["books"; "dune-1965"] = Book_detail "dune-1965"` and `toUrl` round-trips.
- [ ] Importing a result that the server answers with `Duplicate_found` shows the prompt; "add
      anyway" re-sends with `SkipDuplicateCheck = true` (reducer test).
- [ ] A successful import closes the modal and navigates to `/books/{slug}`.
- [ ] Both source badges use the same badge treatment as the RAWG/Steam badges; the Books tab feels
      identical in rhythm to the Games tab (toggles, grid, spinner). [human-eye]
- [ ] `npm run build`, `npm run test:client` green; `npm test` green (Shared changes).

## Notes

- Template: `games-k3vps` (source toggles, merged grid, duplicate prompt reuse) — read its done
  file and `SearchModal.fs`'s Games branches before writing anything.
- Paper overlay / modal chrome per ADR-0016 (`ModalPanel`, `DesignSystem.paperOverlay`); the modal
  already conforms — do not restyle it.
- The Audible source works without an auth file (ADR-0074 §5); do not gate the checkbox on
  `getAudibleStatus`.
- Keep this task free of the detail page's concerns; it only needs the route case to navigate.
