---
id: books-n8fpz
title: Collapse the three per-type content-block method families (Books, Series, Games) into the one generic IMediathecaApi family — they are byte-identical copies over the single ContentBlocks aggregate; BookDetail and SeriesDetail call the generic methods for all seven operations, and the never-called Games family goes
status: backlog
type: refactor
context: books
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [books, content-blocks, tech-debt, series, games]
related_adrs: []
related_research: []
prior_art: [books-y9kxy, books-f33e2]
---

## Why

Content blocks already have exactly one set of events and one behavior regardless of which entity
they sit on: one `ContentBlocks` aggregate (`decide`/`evolve`/`Serialization` in
`src/Server/ContentBlocks.fs`), one stream-naming rule (`ContentBlocks.streamId slug`, bare slug), one
projection table (`content_blocks`, keyed by the oddly-named `movie_slug` column). That is the shape the
builder wants — "a single set of events that also have the same behavior because the content blocks
behave exactly the same regardless in which entity they reside" (refinement 2026-09-16).

What is *not* single is the RPC surface. `IMediathecaApi` carries the eight generic methods
(`addContentBlock`/`updateContentBlock`/`removeContentBlock`/`changeContentBlockType`/
`reorderContentBlocks`/`getContentBlocks`/`groupContentBlocksInRow`/`ungroupContentBlock`) **plus three
per-type families** — `*SeriesContentBlock*`, `*GameContentBlock*`, `*BookContentBlock*` — each a
four-method copy (get/add/update/remove) whose `Api.fs` bodies are byte-identical to the generic ones
with `sessionId = None` hard-wired. None of them adds behavior, an owner-kind key, or a different stream.

The original capture (filed by the `books-f33e2` worker) framed this as "Books' family is missing four
operations, so BookDetail falls back to the generic methods for change-type/reorder/group/ungroup —
risky if streams are ever scoped per media type". The refinement pass established that framing is not
Books-specific and the risk is a half-migration nobody should do:

- `SeriesDetail/State.fs` has the *identical* 3-of-7 split (Series family for add/update/remove, generic
  for the other four). Books copied Series' precedent exactly, as `books-y9kxy` instructed.
- The Games family (`get/add/update/removeGameContentBlock`) has **no caller anywhere** in `src/Client`
  or `tests/` — GameDetail uses the separate Game Journal block document, not content blocks.
- The three `get*ContentBlocks` variants have no client caller either: `BookDetail`/`SeriesDetail` load
  blocks through the `ContentBlocks` field embedded in `getBook`/`getSeries`' detail DTOs.
- Any future per-media-type scoping would have to change `ContentBlocks.streamId` (an event-stream
  rename, i.e. a live-data migration) and would therefore change *every* family at once. There is no
  world where the per-type wrappers protect Books from the generic methods drifting — they'd drift
  together or not at all.

So the right fix is the opposite of completing the Book family: delete all three per-type families and
let every detail page speak to the one generic family, so the API surface matches the single-aggregate
reality. Builder chose this direction (2026-09-16) over (a) adding the four missing Book methods, (b)
Books-only removal with Series/Games as a follow-up, and (c) introducing an owner-kind key now.

## What

Behavior-preserving refactor across the RPC contract, its server implementation, and two client pages.
No event, stream id, aggregate, projection, or live-data change.

1. **`src/Shared/Shared.fs`** — remove the twelve per-type members from `IMediathecaApi`:
   `getSeriesContentBlocks`, `addSeriesContentBlock`, `updateSeriesContentBlock`,
   `removeSeriesContentBlock` (≈ lines 2023–2026); `getGameContentBlocks`, `addGameContentBlock`,
   `updateGameContentBlock`, `removeGameContentBlock` (≈ 2059–2062); `getBookContentBlocks`,
   `addBookContentBlock`, `updateBookContentBlock`, `removeBookContentBlock` and their "fourth parallel
   family" comment (≈ 2160–2165). The eight generic members under `// Content Blocks` keep their exact
   signatures. Replace the removed comment with one line stating the rule: content blocks on any media
   type go through the generic family; the aggregate is the single owner of behavior.
2. **`src/Server/Api.fs`** — delete the twelve matching implementations (Series ≈ 3707–3765, Games
   ≈ 4148–4206, Books ≈ 4505–4563). Nothing else in `Api.fs` changes; the generic implementations
   (≈ 2350–2465) are already the single code path.
3. **`src/Client/Pages/BookDetail/State.fs`** — `Add_content_block`, `Update_content_block`,
   `Remove_content_block`, and the second `addBookContentBlock` call inside the paste/upload handler
   (≈ line 273) switch to `api.addContentBlock model.Slug None …`, `api.updateContentBlock`,
   `api.removeContentBlock`. The four generic calls already there stay. Delete the seven-line "known
   limitation … safely reuse the generic methods" comment (≈ 212–218) and replace it with one line:
   content blocks use the generic family on every media type (single `ContentBlocks` aggregate).
4. **`src/Client/Pages/SeriesDetail/State.fs`** — same three-plus-one call-site switch (≈ 357, 364,
   371, 408).
5. **Test** — one Expecto test in `tests/Server.Tests/BooksApiTests.fs` (the harness already registers
   `ContentBlockProjection.handler`): `addContentBlock bookSlug None request` then `getBook bookSlug`
   returns the block in `ContentBlocks`; and the same through a series slug via `getSeries` (or the
   `SeriesProjectionReadsTests` harness if that fits better). This is the proof that the generic family
   serves every media type end to end — the thing the deleted wrappers used to imply.
6. **Books README delta** (reported in the RESULT block, applied by the conductor): one bullet under
   "Relationships with other contexts" — content blocks on a book are Curation's shared `ContentBlocks`
   aggregate reached through the generic content-block API; there is no Books-specific method family.
   No ubiquitous-language term is added or removed.

Out of scope, deliberately: the latent cross-media-type slug collision (`books-y9kxy` Notes — a movie
and a book with an identical `title-year` slug would share one stream). That is an owner-*identity*
question, not a behavior one; it needs a stream-id decision with a live-data migration and belongs to
Curation if it is ever taken up. This task neither fixes nor worsens it, and leaves one family to
extend instead of four if it is.

## Acceptance criteria

- [ ] `grep -rn "SeriesContentBlock\|GameContentBlock\|BookContentBlock" src tests --include=*.fs`
      (excluding `bin/`/`obj/`) returns no lines.
- [ ] `IMediathecaApi` still declares the eight generic content-block members with unchanged
      signatures: `addContentBlock: string -> string option -> AddContentBlockRequest -> …`,
      `updateContentBlock`, `removeContentBlock`, `changeContentBlockType`, `reorderContentBlocks`,
      `getContentBlocks`, `groupContentBlocksInRow`, `ungroupContentBlock`.
- [ ] `BookDetail/State.fs` and `SeriesDetail/State.fs` each call exactly the generic methods for all
      seven editor operations (add/update/remove/change-type/reorder/group/ungroup), with `None` as the
      session id where the signature takes one; the BookDetail "known limitation" comment is gone and a
      one-line rule comment stands in its place.
- [ ] `src/Server/ContentBlocks.fs`, `src/Server/ContentBlockProjection.fs`, and
      `tests/Server.Tests/ContentBlocksTests.fs` have no diff — the aggregate, events, stream id and
      projection are untouched.
- [ ] A new Expecto test proves a block added through the generic `addContentBlock` against a book slug
      is returned by `getBook`'s `ContentBlocks`, and the same for a series slug through `getSeries`.
- [ ] `npm run build` compiles clean (Fable catches any leftover per-type call), Expecto is green, and
      `npm run test:client` is green.
- [ ] The books BC README gains the one-bullet content-block note described in What §6; no term or
      invariant is removed.

## Notes

- **Origin.** Filed by the `books-f33e2` worker as a follow-up; its premise ("only 3 of 7 operations
  have a book-specific wrapper, so the fallback is fragile") was checked against the code on
  2026-09-16 and reframed — see Why. The `books-y9kxy` instruction "add a fourth parallel family,
  mirroring the Game/Series precedent exactly — do not invent an owner-kind concept" was right to
  refuse an owner-kind key and wrong only in copying the precedent instead of deleting it.
- **Why no ADR.** No new decision: the single aggregate already *is* the design, and this task removes
  an accidental API-level duplication of it. If the owner-identity question is ever taken up, that is
  the ADR (Curation).
- **Fable.Remoting routes.** Deleting interface members deletes their `/api/IMediathecaApi/<name>`
  routes. Single-user app, client and server ship together (Docker), so no compatibility shim.
- **Series/Games READMEs** name no content-block family, so no README delta there; only the books
  README bullet in What §6.
- **Not verified on live data:** whether any cross-media-type slug collision currently exists in
  `mediatheca.db` (no sqlite CLI on the dev box). Irrelevant to this task's correctness — it changes no
  keying — but worth a one-off query if the owner-identity question is ever opened.
