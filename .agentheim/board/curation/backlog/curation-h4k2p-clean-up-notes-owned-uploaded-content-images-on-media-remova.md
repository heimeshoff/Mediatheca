---
id: curation-h4k2p
title: Clear the Notes document (via an ordinary `Notes_saved []` event, never an imperative row delete) and delete its `content/`-prefixed uploaded images when a movie, series, game, or book is removed — a shared helper called from all four `removeX` handlers, restoring the parity the deleted `GameJournal.deleteForGame` gave games
status: backlog
type: chore
context: curation
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [notes, images, cleanup]
related_adrs: [0080, 0043, 0045, 0025]
related_research: []
prior_art: [curation-j4qqt, curation-h98ve]
---

## Why

`GameJournal.deleteForGame` (deleted by curation-j4qqt) deleted a game's uploaded `content/`
journal images on removal by iterating its blocks. Notes, its ADR-0080 successor, has no
equivalent for any of the four media types. All four removal handlers exist — `removeMovie`
(`src/Server/Api.fs` ~2083), `removeSeries` (~3181), `removeGame` (~3702), `removeBook` (~4067);
the authoring-time note that only Series had one was wrong — and each cascades into catalog-entry
removal and poster/backdrop deletion after its aggregate's `Ok ()`, but none touches the item's
`Notes-{token}-{slug}` document or its `notes_blocks`-referenced `content/` uploads.

Two consequences: `content/*` files leak to disk forever, and the ghost `notes_blocks` rows —
unreachable by any read path once the aggregate is `Removed` — keep protecting those leaked files
from ADR-0025's orphan-image scan, because `Administration.imageRefColumns` lists
`("notes_blocks", "image_ref")` and the scan counts them as still referenced.

## What

One new private helper in `src/Server/Api.fs` (sibling to `executeCommandCore`, which is
module-private to `Api.fs` — the helper cannot live in `Notes.fs` or `NotesProjection.fs`),
e.g. `clearNotesOnRemoval conn imageBasePath projectionHandlers mediaType slug`, called from each
of the four `removeX` handlers right after the aggregate's `Ok ()`, alongside the existing
best-effort catalog-entry and poster/backdrop cascade. In this order:

1. **Read first.** `NotesProjection.getForOwner conn mediaType slug`, keep every `ImageRef`
   that starts with `content/`. This must happen before step 2 — the projection's rows for this
   owner vanish the instant `Notes_saved []` is handled.
2. **Clear the document through the event log.** `executeCommand` on `Notes.streamId mediaType
   slug` with `Notes.Save_notes []` (same idiom as `saveNotes`). `decide`'s existing rule yields
   `Notes_saved []` when there was content and `Ok []` (append nothing, create no stream) when
   there was none. Never an imperative `DELETE FROM notes_blocks` — a projection rebuild would
   resurrect the rows (ADR-0080 §6), and the user's writing stays recoverable in the stream
   (ADR-0043: their own words are an observation, not a cache). No new event type — an empty
   snapshot already means exactly "document now empty".
3. **Delete the files last.** `ImageStore.deleteImage imageBasePath ref` for each collected
   ref, exceptions swallowed like the old code. After the append, so a concurrency conflict on
   step 2 does not orphan files that live state still references. Files are cache tier and are
   deleted imperatively at command time, never on projection replay (ADR-0045, mirroring the
   series season/episode cache cleanup in `removeSeries`).

Scoping: only `content/`-prefixed `ImageRef` values, exactly as `GameJournal.deleteForGame`
scoped it — never `posters/`, `backdrops/`, `covers/`, `stills/`. The whole step is best-effort
and non-transactional, matching the existing cascade style (`|> ignore`; a failure here does not
fail the removal).

## Acceptance criteria

- [ ] Removing a movie, series, game, or book whose Notes document holds `content/`-prefixed
      uploaded images deletes those image files from the image store.
- [ ] The removal appends a `Notes_saved []` event to that item's `Notes-{token}-{slug}` stream
      (assertable via `EventStore`), and the prior snapshot(s) remain in the event log — no
      imperative `notes_blocks` delete anywhere in the change.
- [ ] After removal, `NotesProjection.getForOwner` for that `(MediaType, slug)` returns `[]`.
- [ ] Posters, backdrops, and covers are never touched by this cleanup; the existing
      poster/backdrop deletion behaviour is unchanged.
- [ ] Removing a media item with no Notes content completes cleanly: no error, no `Notes_saved`
      event appended, no Notes stream created, no files deleted.
- [ ] Removing one item leaves every other item's Notes content, images, and `notes_blocks`
      rows untouched (owner isolation).
- [ ] Expecto coverage at the API level (`Api.create` through `removeGame`, with a real temp
      image directory per `AudibleApiTests.fs`'s `withTempImageDir` pattern — not the
      `noImagesDir` stub `CatalogProjectionTests.fs` uses) mirroring the deleted
      `GameJournalTests.fs` case "removes the game's blocks and its uploaded content images,
      leaving other games alone", plus the no-Notes no-op case. The old test is recoverable via
      `git show 59c86f4^:tests/Server.Tests/GameJournalTests.fs` (lines 112-143).
- [ ] `npm test` is green.

## Notes

- Call sites: `removeMovie` (`Api.fs` 2083-2116), `removeSeries` (3181-3230), `removeGame`
  (3702-3734), `removeBook` (4067-4097). Each already binds `executeCommand`; the projection list
  is named `movieProjections` in `removeMovie` and `projectionHandlers` elsewhere, but both are
  the same list including `NotesProjection.handler` (`Composition.fs` ~326) — pass it into the
  helper.
- Old implementation for reference: `git show 59c86f4^:src/Server/GameJournal.fs` lines 112-122
  (`deleteForGame`); old `removeGame` call site at `git show 59c86f4^:src/Server/Api.fs:3910`.
- Test bootstrap gap: the local `createApi`/`bootstrap`/`allProjectionHandlers` helpers in
  `CatalogProjectionTests.fs` (18-30) do not register `NotesProjection.handler`. The new test
  needs its own bootstrap that includes it, a real temp image dir, Notes content seeded via
  `api.saveNotes` (or `NotesProjectionTests.fs`'s `appendNotesEvent` pattern), and an actual
  dummy file written under `content/` so `ImageStore.deleteImage` has something real to assert on.
- `Notes.Serialization.toEventData` hardcodes `Metadata = "{}"`, so a cascade-emitted
  `Notes_saved []` looks the same in the event browser as the user emptying the document by hand.
  Accepted: the sibling `*_removed_from_library` / `Game_removed` event at the same timestamp
  disambiguates. Not in scope to add metadata.
- No ADR: this applies ADR-0080 §5/§6, ADR-0043's re-derivability doctrine, and
  ADR-0025/0045's file-cache-tier discipline; it decides nothing new.
- README delta for the worker to report (curation "Notes" entry or an invariant line): *On
  removal of a movie/series/game/book, its Notes document is cleared via an ordinary
  `Notes_saved []` event (never an imperative row delete) and its `content/`-prefixed uploaded
  images are deleted from the image store; posters/backdrops/covers are untouched.*
