---
id: books-n8fpz
title: Books' content-block method family is missing changeContentBlockType/reorderContentBlocks/groupContentBlocksInRow/ungroupContentBlock — BookDetail's ContentBlockEditor wiring falls back to the generic bare-slug-keyed IMediathecaApi methods for those four operations
status: backlog
type: chore
context: books
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [books, content-blocks, tech-debt]
related_adrs: []
related_research: []
prior_art: []
---

## Why

`books-y9kxy` added a book-specific content-block method family
(`getBookContentBlocks`/`addBookContentBlock`/`updateBookContentBlock`/`removeBookContentBlock`) so
Books wouldn't need an "owner key" the way the generic `addContentBlock`/`updateContentBlock`/
`removeContentBlock` family does. But the family is incomplete: there is no
`changeBookContentBlockType`, `reorderBookContentBlocks`, `groupBookContentBlocksInRow`, or
`ungroupBookContentBlock`. `books-f33e2`'s `ContentBlockEditor.view` wiring needs all seven operations
(add/update/remove/change-type/reorder/group/ungroup), so it falls back to the generic,
bare-slug-keyed `IMediathecaApi` methods for the four missing ones — which happens to work correctly
today only because the underlying `ContentBlocks` event stream is shared bare-slug-keyed across every
media type (a limitation the 2026-09-16 refinement pass already recorded: "ContentBlocks streams are
bare-slug-keyed across media types"). If that limitation is ever fixed by scoping content-block streams
per media type, this fallback would silently start operating on the wrong stream for Books specifically,
since only 3 of 7 operations have a book-specific wrapper today.

## What

Either (a) add the four missing book-specific methods to `IMediathecaApi`/`Api.fs`, mirroring the
existing `addBookContentBlock`/`updateBookContentBlock`/`removeBookContentBlock` shape, and repoint
`BookDetail/State.fs`'s `Change_content_block_type`/`Reorder_content_blocks`/`Group_content_blocks`/
`Ungroup_content_block` handlers at them, or (b) if the bare-slug-keying limitation is fixed directly
instead (making a per-BC method family redundant), remove the book-specific family entirely and have
Books use the generic methods throughout, and update the books BC README's note accordingly. Either
resolution should leave exactly one clearly-documented answer to "which methods does Books' content-block
wiring call, and why."

## Acceptance criteria

- [ ] `BookDetail/State.fs` has no call to a generic (non-book-specific) `IMediathecaApi` content-block
      method left unexplained by a code comment, whichever direction (a)/(b) above is chosen.
- [ ] A test (server-side Expecto or client-side Fable.Mocha, whichever fits the chosen direction) proves
      the choice.