---
id: books-g7g1j
title: Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route
status: done
type: feature
context: books
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, integration-dhctm, books-f33e2, design-system-001-formalize-styleguide]
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
  `DuplicatePrompt` ("open existing" / "add anyway" / cancel). `PendingGameImport`
  (`SearchModal.fs`, currently `FromRawg of AddGameRequest | FromSteam of AddGameFromSteamRequest`,
  used only as the third element of `Model.DuplicatePrompt`) needs just two new cases —
  `FromOpenLibrary of AddBookFromOpenLibraryRequest | FromAudible of AddBookFromAudibleRequest` — this
  is a two-line diff, not a redesign; renaming the type to `PendingImport` is cosmetic only (nothing
  outside this file matches on the type name) and optional. Do **not** introduce a separate
  `PendingBookImport` type. "Add anyway" re-sends with `SkipDuplicateCheck = true`. `Import_completed
  (Ok (slug, MediaType.Book))` navigates to `Book_detail slug`.
- Library tab: `loadSearchLibraryCmd` also fetches `api.getBooks`; `filterLibrary` includes books
  (`MediaType.Book`, cover as poster); `Navigate_to (slug, Book)` → `/books/{slug}`.
- `Router.fs`: this task now `depends_on` `books-f33e2`, which owns adding `Book_detail of slug`,
  the `["books"; slug]` parse arm, `toUrl`, and `isDashboardSection` (both tasks originally specified
  the identical ~10-line `Router.fs` edit independently — refined 2026-09-16 to avoid a duplicate-
  union-case compile error at squash time). By the time this task runs, the route already exists;
  confirm it compiles and navigate through it. Only add the `Router.fs` case yourself if `git log`
  shows `books-f33e2` has not in fact landed it (grep `Book_detail` in `Router.fs` first).

## Acceptance criteria

- [ ] `SearchModal.test.fs` (Fable.Mocha): `filterLibrary` returns a book by fuzzy title with
      `MediaType = Book`; the Books-tab reducer seam (extract `applyBooksSearchResults`) merges Open
      Library and Audible results and drops stale results by `SearchVersion`; toggling Audible off
      clears `AudibleResults` and skips the Audible command (assert on the returned `Cmd` list being
      empty for that source, or on a pure `booksSearchPlan` function returning `[OpenLibrary]`).
- [ ] `Route.test.fs`: `parseUrl ["books"; "dune-1965"] = Book_detail "dune-1965"` and `toUrl`
      round-trips — this criterion is almost certainly already satisfied by `books-f33e2` (this task's
      new dependency); confirm rather than duplicate the implementation.
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
- **Scheduling note (added during refinement, 2026-09-16):** `books-f33e2` was added to this task's
  `depends_on` solely to resolve the `Router.fs` duplicate-edit collision above; there is no other
  ordering requirement between the two tasks' actual features (search-modal import vs. detail-page
  rendering are independent once the route exists).

## Outcome

Added the Books tab to the search modal, mirroring games-k3vps's source-toggle template exactly.

**`src/Client/Components/SearchModal.fs`**: `SearchTab` gains `Books`. `PendingGameImport` (kept
under its existing name per the task's own note — renaming to `PendingImport` is cosmetic-only and
was skipped to minimize the diff) gains `FromOpenLibrary of AddBookFromOpenLibraryRequest` and
`FromAudible of AddBookFromAudibleRequest`. Two new pure helpers make the duplicate-prompt flow
generalize cleanly across all four import kinds and are directly unit-tested:
- `pendingImportMediaType` — which library media type a pending duplicate's "Open existing" button
  should navigate to. This also **fixes a pre-existing bug**: the "Open existing" button was
  hardcoded to `dispatch (Navigate_to (existingSlug, MediaType.Game))` regardless of what kind of
  duplicate triggered the prompt — harmless while only Games used `DuplicatePrompt`, but wrong the
  moment Books duplicates need it too.
- `forceDuplicateImport` — "add anyway" always resubmits the exact request that triggered
  `Duplicate_found` with `SkipDuplicateCheck` forced true; pulled out of four inlined
  `{ request with SkipDuplicateCheck = true }` copies in `State.fs` into one pure, per-case-tested
  function.

The Books tab's own reducer seams: `BookSearchResponse` (`OpenLibraryResponse` /
`AudibleResponse`) and `applyBooksSearchResults (version) (response) (model)` — the stale-drop
guard a completed search response needs. Unlike RAWG/Steam (which only ever fire under
`Debounce_tmdb_expired`'s own version check), the Books tab's toggle handlers fire searches outside
that single choke point too, so `OpenLibrary_search_completed`/`Audible_search_completed` carry the
`SearchVersion` the search was fired under, and `applyBooksSearchResults` drops the response if the
model's `SearchVersion` has since moved on (further typing). `BookSource`/`booksSearchPlan
(includeOpenLibrary) (includeAudible)` is the pure decision core for "which sources need firing" —
`State.fs`'s new `booksSearchCmds` turns its output into the actual `Cmd.OfAsync.either` calls. The
view gets a `BookSearchEntry` union (mirrors `GameSearchEntry`) merging Open Library/Audible results
into one keyboard-navigable grid, gated on `IncludeOpenLibrary`/`IncludeAudible` exactly like the
Games tab's "current toggle state wins" principle; both sources dedup against `LibraryBooks` by
name+year, matching the RAWG/Steam/library-dedup pattern already used elsewhere in the file. Audible
cards show a narrator + runtime subtitle line (`audibleSubtitle`) since Audible search results have
no hover-preview endpoint to show that detail in instead (Open Library cards just show the year).
The search placeholder text was updated to mention books.

**`src/Client/State.fs`**: `booksSearchCmds` (parallels `gamesSearchCmds`), `Tab_changed` /
`Query_changed` / `Debounce_tmdb_expired` extended with Books-tab branches identical in shape to
the existing Games-tab ones, `Toggle_include_openlibrary`/`Toggle_include_audible` (mirrors
`Toggle_include_rawg`/`Toggle_include_steam`), `Import_openlibrary`/`Import_audible` (build
`AddBookFromOpenLibraryRequest`/`AddBookFromAudibleRequest`, route `Duplicate_found` through the
existing `Duplicate_prompt_show` flow), and `Duplicate_prompt_force_add` extended with
`FromOpenLibrary`/`FromAudible` arms (refactored to call `SearchModal.forceDuplicateImport` instead
of inlining the `SkipDuplicateCheck = true` copy). `AddBookOutcome.Duplicate_found` needed explicit
qualification in four spots — `Duplicate_found` bare resolves to `AddGameOutcome.Duplicate_found`
per Shared.fs's documented declaration-order collision, even inside a lambda whose parameter type
is pinned by the calling API signature.

Everything the task described as already-landed was confirmed rather than re-implemented: the
Library tab already fetches/filters/navigates books (`loadSearchLibraryCmd`, `filterLibrary`,
`Navigate_to`/`Import_completed`'s `MediaType.Book` cases), and `Router.fs`'s `Book_detail` route
(books-f33e2) already exists with `Route.test.fs` coverage for `parseUrl`/`isDashboardSection` — no
duplicate edit was made to either.

**Tests** (`src/Client/Components/SearchModal.test.fs`, Fable.Mocha via `npm run test:client`): 9
new tests — `filterLibrary` tagging a book match `MediaType.Book`; `applyBooksSearchResults`
merging both sources into the Model independently and dropping a stale-version response;
`booksSearchPlan` for both/one/neither source checked; `Duplicate_found` on a Books import showing
the prompt with a `FromOpenLibrary` pending import and `forceDuplicateImport` flipping
`SkipDuplicateCheck`, repeated for `FromAudible`; and `pendingImportMediaType` routing Books vs.
Games duplicates correctly. 91/91 client tests pass (13 files); `npm run build` (Fable compile
gate) passes; `npm test` (859 Expecto tests, untouched server code) passes.

The "Both source badges use the same badge treatment..." and "the Books tab feels identical in
rhythm to the Games tab" acceptance criteria are `[human-eye]` per the task — verified by direct
structural mirroring of the Games tab's toggle row, grid, badge and spinner code, not by an
automated visual check.

Key files: `src/Client/Components/SearchModal.fs`, `src/Client/Components/SearchModal.test.fs`,
`src/Client/State.fs`.
