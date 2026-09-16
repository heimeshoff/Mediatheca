---
id: curation-h4k2p
title: Clean up Notes-owned uploaded content images on media removal (parity with the deleted GameJournal.deleteForGame cleanup)
status: backlog
type: chore
context: curation
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [notes, images, cleanup]
related_adrs: [0080]
related_research: []
prior_art: []
---

## Why

The old `GameJournal.deleteForGame` (deleted by curation-j4qqt) cleaned up a game's
uploaded-to-`content/` journal images when the game itself was removed from the library —
`Api.fs`'s `removeGame` called it explicitly. Notes has no equivalent: neither curation-h98ve
(Notes server core) nor curation-knqfj (Notes editor) nor curation-j4qqt (this migration) added
an analogous cleanup step for `notes_blocks.image_ref` values when a movie, series, game, or book
is removed. This is a real regression for games specifically (they used to get this cleanup) and
a pre-existing, never-closed gap for movies/series/books (Notes shipped there with no removal
cleanup at all, presumably because those media types may not even have a removal command yet —
worth checking during refinement).

## What

For each media type whose aggregate has a `Remove_*` command, delete every `notes_blocks` row's
`ImageRef` value under `content/` for that owner's `(MediaType, slug)` before (or as part of) the
removal, mirroring `GameJournal.deleteForGame`'s "only content/ uploads, never posters/backdrops"
scoping. Confirm which media types currently have a removal path at all (the task grep at
authoring time found only `Series.Remove_series`) — this task's scope may need narrowing to just
the media types that actually support removal today.

## Acceptance criteria

- [ ] Removing a game (or any other media type with a removal command) deletes its
      `notes_blocks`-referenced `content/`-prefixed uploaded images from the image store.
- [ ] Posters/backdrops/covers are never touched by this cleanup.
- [ ] A media item with no Notes content removes cleanly (no-op, no error).
- [ ] Expecto coverage mirroring the deleted `GameJournalTests.fs`'s "removes the game's blocks
      and its uploaded content images, leaving other games alone" case.