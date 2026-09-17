---
id: books-depwh
title: The book detail page's Details card no longer shows a date — the metadata line under the description reads just `Publisher · Language`, dropping the published/release date that Audible imports put at its end
status: doing
type: feature
context: books
created: 2026-09-17
completed:
depends_on: [design-system-001-formalize-styleguide]
blocks: []
tags: [books, audible, detail-page, frontend]
related_adrs: []
related_research: []
prior_art: [books-f33e2]
---

## Why

After importing a book from Audible, the Details card on `/books/{slug}` ends its metadata
line with a date (Audible's `release_date`, which the builder reads as a published or
purchase date). The builder doesn't want that date on the card at all.

## What

In `src/Client/Pages/BookDetail/Views.fs` `detailsCard`, the metadata line currently joins
`[ book.Publisher; book.PublishedDate; book.Language ]` with ` · `. Drop `PublishedDate` from
that list **and** from the `hasMeta` visibility check, so the line is `Publisher · Language`
and a book whose only metadata is a date shows no line at all.

This is display-only and applies to every book, whatever its source (Audible, Goodreads,
Open Library) — the card doesn't know where its data came from. The published date stays in
`book_metadata_cache`, `BookDetail.PublishedDate`, and the adapters, so it's still there if a
future view wants it. No server, Shared, or schema change.

## Acceptance criteria

- [ ] `detailsCard` no longer renders `book.PublishedDate` — the metadata line holds only the present values of `Publisher` and `Language`, joined by ` · `.
- [ ] A book with a `PublishedDate` but no `Publisher` and no `Language` renders no metadata line (the `hasMeta` check ignores the date).
- [ ] The rest of the Details card is unchanged: description, subject chips, average rating.
- [ ] `BookDetail.PublishedDate` and the `published_date` cache column are still there — no Shared, server, or migration changes.
- [ ] `npm run build` succeeds.
- [ ] On an Audible-imported book's detail page, the Details card shows no date. [human-eye]

## Notes

- **Overlap with `books-nvnyk` (in `doing/`):** that task changes the description rendering in the
  same `detailsCard` function. The edits touch different lines, but whoever integrates second should
  expect a small merge in `detailsCard`.
- Purchase date (`purchase_date` in `Audible.fs`'s library item) isn't carried into the book
  metadata at all — the date on the card is the release date written by `Api.fs`'s Audible paths
  (`PublishedDate = product.ReleaseDate` / `item.ReleaseDate`).
