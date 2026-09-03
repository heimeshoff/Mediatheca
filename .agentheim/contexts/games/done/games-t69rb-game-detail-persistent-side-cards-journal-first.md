---
id: games-t69rb
title: Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise
status: done
type: feature
context: games
created: 2026-09-03
completed: 2026-09-03
depends_on: [design-system-001]
blocks: []
tags: [game-detail, journal, tabs, layout, frontend]
related_adrs: []
related_research: []
prior_art: []
---

## Why

On a game's detail page the Overview tab renders a two-column layout: trailers,
description and the rest of the details on the left, and a stack of cards on the
right (External Links, play facets, friends / family owners, catalogs, …). The
Journal tab replaces the *whole* grid with the block editor, so every one of those
cards disappears the moment the builder starts writing. The cards are the game's
standing dossier — they should stay put while the content beside them changes.

Second, for a game that already has journal content, the journal *is* the reason
the builder opens the page: the Overview is a metadata sheet, the journal is what
they wrote. Landing on Overview and clicking through to Journal every time is a
tax paid on exactly the games the builder cares most about.

## What

Restructure `src/Client/Pages/GameDetail/Views.fs` so the two-column grid is the
page frame and the tab only selects what fills the **content column**:

- The right-hand column (today the `lg:col-span-4` block, built inside the
  `| Overview ->` arm) moves out of the tab match and is rendered unconditionally
  beside the content column. The Overview details and the `JournalEditor.view`
  become the two possible fillings of the left / content column.
- The tab bar keeps its two tabs, Overview and Journal, and keeps its current
  `Set_tab` semantics. Whether it sits above the whole grid or inside the content
  column is the worker's call; the constraint is that the card column neither
  unmounts nor moves when the tab changes.
- Default tab rule (games-t69rb): when a game is opened, the page lands on
  **Journal** if that game's journal document has content, and on **Overview**
  otherwise. "Has content" means the block list returned for the game contains at
  least one block that is not blank: non-whitespace `Content`, or an `ImageRef`
  / `Url` set (image and link blocks carry no text). A document consisting only
  of empty text blocks counts as empty.
- The rule is applied **once per game load** — when the slug's data first
  arrives — never on the refreshes that `Game_loaded` triggers after every
  command (status change, rating, friend edits, facet overrides all re-fetch the
  detail). A tab the builder picked by hand must survive those refreshes.
- Recommended way to know "has content" without a second round-trip: add a
  `HasJournalContent: bool` field to the `GameDetail` DTO in `src/Shared/Shared.fs`,
  computed server-side in `getGameDetail` (`src/Server/Api.fs`) from
  `game_journal_blocks` via `GameJournal.fs` (an EXISTS-style query with the
  blank-block rule above, or `GameJournal.get` filtered — the table is small).
  Acceptable alternative: probe `getGameJournal` from `init` and decide the tab
  when the probe answers, as long as the once-per-load rule holds. Either way
  the `JournalEditor` keeps owning its own document load and save.

Out of scope: Series (`SeriesDetail` has Overview/Episodes, no Journal tab) and
Movies (no tabs). The journal-first default is a Games-page behaviour.

## Acceptance criteria

- [x] On a viewport ≥ `lg`, switching between Overview and Journal on a game
      detail page leaves the right-hand card column rendered and in the same
      position; only the content column's children change. Verifiable via a
      Playwright check (ADR-0027) or a DOM assertion that the card column's
      element is the same node before and after the tab switch.
- [x] On a narrow viewport (single-column grid) the card column is still present
      on the Journal tab, stacked below the journal content, exactly as it stacks
      below the Overview content today.
- [x] Opening a game whose journal document has at least one non-blank block
      lands on the Journal tab; opening a game whose document is empty or
      consists only of blank blocks lands on Overview. Covered by client unit
      tests over the pure decision (`*.test.fs` via Vitest, ADR-0064) for:
      empty list, only-blank text blocks, one text block with content, one image
      block with `ImageRef` and empty `Content`.
- [x] After the builder manually selects a tab, a subsequent `Game_loaded`
      caused by a command on the same page (e.g. changing the status) does not
      change the active tab. Covered by an MVU `update` test.
- [x] Navigating from one game to another (different slug) re-applies the
      default-tab rule for the new game.
- [x] If `HasJournalContent` is added to `GameDetail`, `npm run build` and
      `npm test` pass, and the server computes it from the journal block table,
      not from a new event or projection column (the game journal is plain
      storage, not event-sourced — see the `Shared.fs` comment above
      `JournalBlockTypes` and ADR-0043's re-derivability test).
- [ ] Switching tabs does not visibly reflow or flash the card column. [human-eye]

## Notes

- Today's structure: `Views.fs` ~L989–1760. The tab bar at ~L993 is followed by
  `match model.ActiveTab with | Overview -> <12-col grid with left col-span-8 and
  right col-span-4> | Journal -> <JournalEditor.view model.Slug>`. The right
  column starts at ~L1180 ("Right Column: Social & Activity", `panelCard`s for
  Links, facets, friends, catalogs, …). `ActiveTab` defaults to `Overview` in
  `State.fs` `init`; `Set_tab` only writes the model, the tab is not in the URL
  (`Router.fs` has `Game_detail of slug` only) — so the default is purely an
  init / first-load concern.
- The tab bar currently hand-rolls its classes. `DesignSystem.underlineTab` /
  `underlineTabActive` (design-system-k9p3v, dir-3a header tabs) is the design
  system's tab primitive — adopt it while the bar is being touched if it is a
  drop-in, but it is not a requirement of this task.
- `JournalEditor` (`src/Client/Components/JournalEditor.fs`) is a self-contained
  React component keyed on `slug` — moving it into the content column must not
  change its mount behaviour (it loads on mount, debounce-saves, flushes a dirty
  save on unmount).
- Styleguide gate: `depends_on` design-system-001 (done) per the games README's
  Frontend gate; review the result against the live StyleGuide page.

## Outcome

Restructured `Pages/GameDetail/Views.fs` so the 12-col grid (`lg:col-span-8`
content column + `lg:col-span-4` card column) is the unconditional page frame;
the `match model.ActiveTab` now lives *inside* the content column's
`prop.children`, wrapping the old Overview content in `React.fragment` (no
extra DOM node, so `space-y-10` sibling spacing is unaffected) and rendering
`JournalEditor.view model.Slug` for the Journal arm. The right ("Social &
Activity") column div is written exactly once in the file, outside any
tab-conditional branch — mounted for both tabs, same DOM node, same position.
The narrow-viewport stacking criterion follows from the same code path: there
is no separate mobile branch, only the existing responsive Tailwind classes
(`grid-cols-1 lg:grid-cols-12`) on that one grid div — confirmed by
inspection, not a separate mobile Playwright run.

Added `HasJournalContent: bool` to `Shared.GameDetail`, computed in
`GameProjection.getBySlug` via a new shared pure function
`JournalBlock.hasContent : JournalBlockDto list -> bool` (Shared.fs, next to
`JournalBlockDto`) applied to `GameJournal.get conn slug` — re-derived fresh
on every call, no new event/column, per ADR-0043's re-derivability test.
`GameDetail/State.fs`'s `Game_loaded` case applies the journal-first default
only when `model.Game` was `None` going in (the page's first load for this
game — root `State.fs` re-runs `Pages.GameDetail.State.init` on every slug
change, which resets `Game` to `None`), leaving `ActiveTab` untouched on every
later refresh, satisfying both the "survives a manual pick" and the
"re-applies on navigation to a different game" criteria without any extra
model state.

Test coverage:
- `Pages/GameDetail/JournalHasContent.test.fs` (Vitest/Fable.Mocha, 4 cases):
  empty list, only-blank text blocks, one non-blank text block, one image
  block with `ImageRef` and empty `Content` — the exact four scenarios the
  acceptance criteria named.
- `Pages/GameDetail/DefaultTab.test.fs` (Vitest/Fable.Mocha, 4 cases): calls
  `GameDetail.State.update` directly with a `Unchecked.defaultof<IMediathecaApi>`
  stand-in (safe here — `Game_loaded`'s reducer branch never touches `api`,
  so faking ~100 unrelated RPC fields wasn't needed) — first-load-journal,
  first-load-no-journal, manual-pick-survives-refresh, and
  re-applies-on-navigation-to-a-new-game.
- `tests/e2e/game-detail-persistent-cards.spec.ts` (Playwright, ADR-0027):
  seeds a game via the hermetic `addGame` API (no TMDB/RAWG/Steam dependency),
  saves one non-blank journal block via `saveGameJournal`, then asserts DOM
  **node identity** (`elementHandle.isConnected` plus a same-reference
  `page.evaluate` check) for the "Links" card-column heading across an
  Overview→Journal tab switch at a 1280×900 (`lg`) viewport — a stale/
  disconnected handle would mean React tore the subtree down and rebuilt a
  lookalike, exactly what this task set out to prevent. Additive-only (one
  new game + one journal save), no destructive-spec skip gate needed. Run
  once locally against a temp `DATA_DIR` (never the live DB) — passed.

Server-side fallout: `GameProjection.getBySlug` now also queries
`game_journal_blocks` (via `GameJournal.get`), which several
`tests/Server.Tests/*.fs` in-memory-connection setups didn't initialize
(only a few files already called `GameJournal.initialize`). Added the missing
`GameJournal.initialize conn` call to the 13 affected test files' setup
helpers — idempotent (`CREATE TABLE IF NOT EXISTS`), no behavior change to
what those files test.

`npm run build`, `npm run test:client` (16/16), and `npm test` (685/685) all
green. Last acceptance criterion (`[human-eye]`, no visible reflow/flash) not
independently verified — left unticked per the task's own marking.

Files: `src/Shared/Shared.fs`, `src/Server/GameProjection.fs`,
`src/Client/Pages/GameDetail/{Views,State}.fs`, `src/Client/Client.fsproj`,
`src/Client/Pages/GameDetail/{JournalHasContent,DefaultTab}.test.fs` (new),
`tests/e2e/game-detail-persistent-cards.spec.ts` (new), 13
`tests/Server.Tests/*.fs` setup fixes, `.agentheim/contexts/games/README.md`.
