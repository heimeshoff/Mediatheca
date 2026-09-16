---
id: curation-knqfj
title: Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3)
status: done
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: [curation-h98ve, design-system-001-formalize-styleguide]
blocks: [curation-j4qqt]
tags: [notes, content-blocks, game-journal, curation, frontend, styleguide, movies, series, games, books]
related_adrs: [0080, 0079]
related_research: []
prior_art: []
---

## Why

With the Notes server core in place (`curation-h98ve`), the client must actually use it. Today
`JournalEditor` (1318 lines, self-contained, games only) and `ContentBlockEditor` (837 lines,
props-driven, movies/series/books) are two editors for two models. After this task there is one
editor, `NotesEditor`, on all four detail pages, and the StyleGuide — the canonical design-system
artifact and frontend gate — shows it. `ContentBlockEditor` retires. The server-side teardown of
ContentBlocks/GameJournal is `curation-j4qqt`, not this task.

## What

- Rename `src/Client/Components/JournalEditor.fs` → `NotesEditor.fs` (module
  `Mediatheca.Client.Components.NotesEditor`). Its view takes `(mediaType: MediaType) (slug: string)`
  instead of a bare game slug; its internal Fable.Remoting proxy calls `getNotes`/`saveNotes` with the
  media type. Debounce, autosave state, slash menu, drag/columns and `Doc.normalize` stay unchanged.
- Mount `NotesEditor.view` on `MovieDetail`, `SeriesDetail` and `BookDetail` Views exactly where
  `ContentBlockEditor.view` sits today (a panel in the content column — those pages have no tabs and
  this task adds none; the games-only Notes-first tab rule stays games-only), and on `GameDetail`'s
  tab exactly where `JournalEditor.view` sits, with the tab label "Journal" → "Notes". `Doc.normalize`
  semantics and the 800 ms debounce are the same on every page.
- Remove the content-block wiring from `MovieDetail`/`SeriesDetail`/`BookDetail` `Types.fs`/`State.fs`
  (the seven `*_content_block*` messages, `Content_block_result`, the `uploadContentImage` screenshot
  path that the editor owned) — the DTOs' `ContentBlocks` fields stay until j4qqt deletes them, simply
  unread.
- Delete `src/Client/Components/ContentBlockEditor.fs` and its two StyleGuide specimens
  (`src/Client/Pages/StyleGuide/Views.fs` ≈ 1919 and ≈ 2137, plus their `Types.fs` state); add one
  NotesEditor specimen in their place, backed by a fixed sample document (no live API call from the
  StyleGuide).
- Update `src/Client/Pages/GameDetail/DefaultTab.test.fs` if h98ve's `HasNotesContent` rename left
  anything for it; add a Vitest case for the `(MediaType, slug)` proxy call shape if the editor exposes
  a testable seam without the DOM.

## Acceptance criteria

- [ ] `npm run build` succeeds.
- [ ] `npm run test:client` is green, including `DefaultTab.test.fs`.
- [ ] `ContentBlockEditor.fs` is deleted and `ContentBlockEditor` has zero references in `src/Client`
      (grep).
- [ ] `NotesEditor.view` is referenced from `MovieDetail`, `SeriesDetail`, `BookDetail` and `GameDetail`
      Views, each passing its own `MediaType` (grep).
- [ ] `src/Client/Pages/StyleGuide/Views.fs` contains exactly one NotesEditor specimen and zero
      ContentBlockEditor specimens (grep).
- [ ] No client call to any `*ContentBlock*` or `*GameJournal` `IMediathecaApi` member remains (grep) —
      the server members still exist until j4qqt, unused.
- [ ] The GameDetail tab reads "Notes" and the editor on a movie, series, book and game page looks and
      behaves as the game journal did (slash menu, drag, columns, autosave indicator). [human-eye]
- [ ] Chrome DevTools smoke: open one detail page per media type, type a block, wait for the autosave
      indicator, reload — the block is back; no console errors. [human-eye]

## Notes

- Read ADR-0080 first. Do not reintroduce a bare-slug prop; the `(MediaType, slug)` pair is the owner.
- Decided at refinement (2026-09-16): movie/series/book pages get a plain Notes panel, not a
  Notes-first tab default — they have no tabs, and inventing tab machinery is out of scope.
- The styleguide dependency is already done; it is listed to satisfy the frontend gate.
- Waits in `backlog/` only because the promote gate needs `curation-h98ve` in `done/`; it is otherwise
  fully refined — promote as soon as h98ve ships.

## Outcome

`NotesEditor` (`src/Client/Components/NotesEditor.fs`, module `Mediatheca.Client.Components.NotesEditor`) replaces both `JournalEditor` and `ContentBlockEditor` as the single Notion-style block editor for all four media types, closing out step 2 of ADR-0080's three-step plan (step 1 was the server core in `curation-h98ve`; step 3, the ContentBlocks/GameJournal server teardown, is `curation-j4qqt`).

Shape of the change:
- The old `view (slug: string)` became a private `editor (source: Source)`, where `type private Source = Live of mediaType: MediaType * slug: string | Demo of initialBlocks: JournalBlockDto list`. Two public entry points wrap it: `view (mediaType: MediaType) (slug: string)` (calls `getNotes`/`saveNotes`, 800ms debounce unchanged) and `viewDemo (initialBlocks: JournalBlockDto list)` (seeds from the given list, "saves" only into local React state, and synthesizes a local `ImageRef` instead of calling `uploadContentImage` for dropped/pasted images) -- so the StyleGuide specimen never makes a live API call, per the task's requirement.
- `Doc.normalize`, the slash menu, drag/drop, column-forming and the debounced-autosave indicator are byte-for-byte the same logic as before; only the persistence and load calls became source-aware. The internal `.journal-block` CSS hook was renamed to `.notes-block` (cosmetic, no visible change) and the loading placeholder now reads "Loading notes…".
- `MovieDetail`, `SeriesDetail` and `BookDetail` each now mount `NotesEditor.view <MediaType> model.Slug` in a plain `Html.section`/`Html.div` exactly where `ContentBlockEditor.view` sat (no tabs added, per the refinement note). Each page's `Types.fs`/`State.fs` lost its seven `*_content_block*` messages (`Add_content_block`, `Update_content_block`, `Remove_content_block`, `Change_content_block_type`, `Reorder_content_blocks`, `Upload_screenshot`/`Screenshot_uploaded`, `Group_content_blocks`/`Ungroup_content_block`, `Content_block_result`) and their `State.fs` handlers; the DTOs' `ContentBlocks` fields are untouched (still populated server-side, simply unread client-side) as `curation-j4qqt`'s job.
- `GameDetail`'s `Journal` tab keeps its `GameTab` DU case and machinery unchanged; only its button label changed from "Journal" to "Notes", and its mount changed from `JournalEditor.view model.Slug` to `NotesEditor.view Game model.Slug`.
- `src/Client/Components/ContentBlockEditor.fs` and the old `src/Client/Components/JournalEditor.fs` are deleted; `Client.fsproj`'s two `<Compile Include>` lines for them collapsed into one `Components\NotesEditor.fs` line, ahead of the page modules that consume it (same position `ContentBlockEditor.fs` held).
- `src/Client/Pages/StyleGuide/Types.fs`'s `Section` DU lost `ContentBlocks`/`ContentZone`, gained `Notes`; `Views.fs`'s two old sections (`contentBlocksSection`/`contentZoneSection`, their two demo components and two `ContentBlockEditor.view` mounts) were replaced by one `notesSection` backed by a fixed `sampleNotesDoc: JournalBlockDto list` (heading, paragraph, two bullets, a todo, a quote) mounted via `NotesEditor.viewDemo sampleNotesDoc`, with an updated block-type table (all 16 `JournalBlockTypes`), API doc block (`NotesEditor.view (mediaType: MediaType) (slug: string)`), interaction-pattern table and two decision boxes.

Verification: `npm run build` (Fable typecheck + production bundle) succeeds; `npm run test:client` is green at 16 files / 108 tests, including the pre-existing `Pages/GameDetail/DefaultTab.test.fs` (4 tests, unchanged -- `HasNotesContent` was already in place from `curation-h98ve`, nothing left for this task to touch there) and `Pages/GameDetail/JournalHasContent.test.fs` (unaffected, tests `Shared.fs`'s `JournalBlock.hasContent`, not this editor); `npm test` (938 Expecto tests) passes unchanged, confirming no server-side behaviour was touched.

Not independently verified (per this task's Windows-dev-machine, no-Chrome-DevTools instructions -- these are the builder's two `[human-eye]` acceptance criteria, not re-verified here): the GameDetail tab visually reading "Notes" and behaving like the old journal (slash menu, drag, columns, autosave indicator all carried over unchanged in code, but not eyeballed in a browser); and the Chrome DevTools smoke test (type a block on one page per media type, wait for autosave, reload, confirm persistence, no console errors).

Key files: `src/Client/Components/NotesEditor.fs`, `src/Client/Pages/{MovieDetail,SeriesDetail,BookDetail,GameDetail}/Views.fs`, `src/Client/Pages/{MovieDetail,SeriesDetail,BookDetail}/{Types,State}.fs`, `src/Client/Pages/StyleGuide/{Types,Views}.fs`, `src/Client/Client.fsproj`.
