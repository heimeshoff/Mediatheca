---
id: curation-cyxbc
title: Books in catalogs — a catalog entry can reference a book (RoutePrefix "books", cover as poster), with `getCatalogsForBook` / add-to-catalog on the book detail page and books in the catalog detail list, the same conformist treatment catalogs give movies, series and games
status: backlog
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: [books-y9kxy, books-f33e2]
blocks: []
tags: [books, catalogs, curation]
related_adrs: []
related_research: []
prior_art: []
---

## Why

Catalogs are "I made a list" across media types; a reading list is the most natural list of all.
Today `CatalogEntryDto` carries a `RoutePrefix` of `movies` / `series` / `games` and the API has
`getCatalogsForMovie` / `…Series` / `…Game`; books need the fourth variant so the book detail page
can show and edit catalog membership like the movie page does.

## What

- `CatalogProjection` / `Catalogs.fs`: accept a book slug as an entry (whatever the entry key shape
  is — extend the `RoutePrefix` vocabulary with `books`, resolve cover/title/year from `book_list`).
- `IMediathecaApi.getCatalogsForBook: slug -> …`, and the add/remove entry commands accept a book ref.
- Book detail page (`Pages/BookDetail`): the Catalogs card + `CatalogManager` modal, copied from the
  movie page.
- Catalog detail page: book entries render with the book cover and navigate to `/books/{slug}`.

## Acceptance criteria

- [ ] Refine before promotion: confirm how `CatalogEntryDto`/`CatalogRef` key entries today (the
      survey found `MovieSlug` + `RoutePrefix`) and whether a rename to a neutral `MediaSlug` is
      in scope.
- [ ] Adding a book to a catalog and reloading the catalog shows it with cover and title; removing it
      removes it; `getCatalogsForBook` lists the catalog.

## Notes

- Captured alongside the Books integration set (2026-09-16). Deliberately excluded from
  `books-f33e2` so the detail page ships without waiting on Curation.
