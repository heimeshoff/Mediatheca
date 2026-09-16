---
id: books-f3sb2
title: Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types
status: todo
type: feature
context: books
created: 2026-09-16
completed:
depends_on: [curation-cyxbc, books-f33e2, design-system-001-formalize-styleguide]
blocks: []
tags: [books, catalogs, detail-page, frontend]
related_adrs: [0079, 0016]
related_research: []
prior_art: [books-f33e2]
---

## Why

A reading list is the most natural catalog of all. `curation-cyxbc` makes a catalog entry typed
and teaches the projection and API about books; this task puts the user-facing half on the book
detail page — see which catalogs a book is in, add it to one, remove it — the same pill row the
movie, series and game pages carry. It also closes a gap the other three types don't have: removing
a book leaves its catalog entries behind.

## What

**Extract, don't paste (client).** `CatalogManager` is a private ~140-line copy in each of
`Pages/MovieDetail/Views.fs` (l. 347), `Pages/SeriesDetail/Views.fs` (l. 301) and
`Pages/GameDetail/Views.fs` (l. 475), differing only in the "already in all catalogs" noun. Lift it
to `src/Client/Components/CatalogManager.fs` (before the pages in `Client.fsproj`) with the
signature the three copies share — `allCatalogs`, `mediaCatalogs`, search text, `onAdd`,
`onRemove`, `onCreate`, `onClose` — plus a `mediaNoun: string` for that one copy string. The three
pages call the component; their private copies are deleted. Paper overlay rules (ADR-0016) are
already satisfied by `ModalPanel.viewCustom`; keep it.

**Book detail page (`Pages/BookDetail/{Types,State,Views}.fs`).**
- Model: `AllCatalogs: CatalogListItem list`, `BookCatalogs: CatalogRef list`,
  `ShowCatalogPicker: bool`, `CatalogSearch: string` (mirror the GameDetail model fields).
- Msgs: `Catalogs_loaded`, `Book_catalogs_loaded`, `Open_catalog_picker`, `Close_catalog_picker`,
  `Catalog_search_changed`, `Add_to_catalog of catalogSlug`, `Remove_from_catalog of catalogSlug *
  entryId`, `Create_catalog_and_add of name`, `Catalog_result` — the GameDetail set.
- `init` / `Load_book` also fire `api.getCatalogs ()` and `api.getCatalogsForBook slug`;
  `Add_to_catalog` / `Create_catalog_and_add` send `{ MediaSlug = slug; MediaType = Book; Note =
  None }`; `Catalog_result (Ok ())` reloads both lists.
- View: the pill row (round "add" button with `Icons.catalog`, one pill per catalog linking to
  `/catalogs/{slug}` with the hover × to remove) at the top of the left column, exactly where
  MovieDetail places it (`Views.fs` l. 1065); the picker modal is the shared component with
  `mediaNoun = "Book"`.

**Server (`src/Server/Api.fs`).** `removeBook`: after `Remove_book_from_library` succeeds, cascade
`CatalogProjection.getEntriesByMediaSlug conn Book slug` → `Catalogs.Remove_entry` per hit, the
same block `removeMovie` / `removeSeries` / `removeGame` carry.

**Catalog detail page** already renders book entries after `curation-cyxbc` (cover, title, link to
`/books/{slug}`); nothing to add there beyond checking it by eye.

## Acceptance criteria

- [ ] `src/Client/Components/CatalogManager.fs` exists; MovieDetail, SeriesDetail, GameDetail and
      BookDetail all render the picker through it and no page keeps a private `CatalogManager`
      (`grep -n "let private CatalogManager" src/Client/Pages` is empty).
- [ ] Vitest (`BookDetail` `State` test): `Load_book` issues the two catalog loads;
      `Add_to_catalog` builds an `AddCatalogEntryRequest` with `MediaType = Book` and the book's
      slug; `Catalog_result (Ok ())` reloads both lists.
- [ ] Expecto through the API: add a book to a catalog, `getCatalogsForBook` lists it; `getCatalog`
      shows the entry with the book's title, cover and `MediaType = Book`; `removeCatalogEntry`
      removes it and `getCatalogsForBook` is empty.
- [ ] Expecto: `removeBook` removes every catalog entry referencing the book and leaves a
      same-slugged movie's entry in place.
- [ ] In the running app: the book detail page shows the pill row, the picker adds and creates,
      the × removes, and the catalog detail page lists the book with its cover, opening
      `/books/{slug}`.
- [ ] The pill row and picker on the book page are indistinguishable in rhythm from the movie
      page's. [human-eye]
- [ ] `npm run build`, `npm test`, `npm run test:client` green.

## Notes

- Split out of `curation-cyxbc` on 2026-09-16 (refinement; ADR-0079 records the typed-entry
  decision). Deliberately kept apart from `books-f33e2` so the detail page shipped without waiting
  on Curation.
- The extraction is the orchestrator's recommendation ("B should extract, not paste") — three
  copies already drift only in one string, and a fourth would make a later fix a four-file edit.
- `getEntriesByMediaSlug` is type-filtered by `curation-cyxbc`; without it a same-slugged
  movie's entry would be deleted with the book (ADR-0079 §4).
