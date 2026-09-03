---
id: games-t69rb
title: Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise
status: doing
type: feature
context: games
created: 2026-09-03
completed:
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

- [ ] On a viewport ≥ `lg`, switching between Overview and Journal on a game
      detail page leaves the right-hand card column rendered and in the same
      position; only the content column's children change. Verifiable via a
      Playwright check (ADR-0027) or a DOM assertion that the card column's
      element is the same node before and after the tab switch.
- [ ] On a narrow viewport (single-column grid) the card column is still present
      on the Journal tab, stacked below the journal content, exactly as it stacks
      below the Overview content today.
- [ ] Opening a game whose journal document has at least one non-blank block
      lands on the Journal tab; opening a game whose document is empty or
      consists only of blank blocks lands on Overview. Covered by client unit
      tests over the pure decision (`*.test.fs` via Vitest, ADR-0064) for:
      empty list, only-blank text blocks, one text block with content, one image
      block with `ImageRef` and empty `Content`.
- [ ] After the builder manually selects a tab, a subsequent `Game_loaded`
      caused by a command on the same page (e.g. changing the status) does not
      change the active tab. Covered by an MVU `update` test.
- [ ] Navigating from one game to another (different slug) re-applies the
      default-tab rule for the new game.
- [ ] If `HasJournalContent` is added to `GameDetail`, `npm run build` and
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
