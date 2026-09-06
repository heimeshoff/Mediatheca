---
id: intelligence-c3vqm
title: Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair
status: todo
type: bug
context: intelligence
created: 2026-09-06
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, frontend, games, posters, sizing, in-focus]
related_adrs: []
related_research: []
prior_art: [intelligence-encn4, intelligence-f6cfv, intelligence-p9m4t]
---

## Why

Two complaints about the All tab's **Games** section, both in
`gamesInFocusPosterSection` / `gameInFocusPosterCard`
(`src/Client/Pages/Dashboard/Views.fs`, ~lines 425–498).

### 1. Game posters resize with the viewport; movie posters don't

The Movies row and the Games section size their posters by two different rules,
and only one of them is bounded:

- **Movies** go through `DesignSystem.filmstripRow` — every tile is
  `flex-[1_0_130px]` inside a poster box that is a **fixed `h-[196px]`**
  (`DesignSystem.fs` ~line 634, the 3a proportions from
  `design-system-wd5zk`). Height never changes, so a movie poster looks the
  same size at every window width.
- **Games** are a `grid grid-cols-2 sm:grid-cols-3 gap-3` of `1fr` columns
  wrapping `.poster-image-container`, which is `aspect-ratio: 2/3`
  (`index.css:526`). Nothing bounds the track width, so each poster's width —
  and therefore its height — is a pure fraction of whatever space the section
  has.

That space swings hard, because the Games/Books split in `allTabView` stays
single-column until `xl`. Between `sm` and `xl` three game posters divide the
**full page width** and grow enormous; at `xl` the section halves and they snap
back down. The result is posters that visibly change size as the window
changes, next to movie posters that never do.

The builder wants the game posters capped at the movie posters' size — a fixed
maximum, not a fraction that grows.

### 2. The crosshair badge on a games card says nothing

`gameInFocusPosterCard` renders the gold crosshair badge **unconditionally**
(no `if item.InFocus`, unlike `movieToWatchPosterCard`, which guards it). It
can't be conditional: for games, `InFocus` *is* the status, and this section
lists exactly the InFocus games. So the badge fires on every card and
distinguishes nothing.

This is the same call `intelligence-f6cfv` already made for the "Next episode"
card — placement already carries the In Focus meaning, so the badge is
repeating it. The Movies row is the case where the badge still earns its
place: "Movies to Watch" genuinely mixes In Focus and non-In Focus items, so
there the flag is information.

## What

One file: `src/Client/Pages/Dashboard/Views.fs`. No shared-type change, no
server change, no `DesignSystem.fs` change.

### 1. Cap the poster size in `gamesInFocusPosterSection`

Replace the fraction-based `grid grid-cols-2 sm:grid-cols-3` with a layout
whose tiles have a hard maximum matching the filmstrip poster — **196px tall,
~130px wide** (the 2/3 aspect ratio makes 130 × 195 the same visual size as a
movie tile).

An auto-fill track is the straightforward shape:
`grid-cols-[repeat(auto-fill,minmax(0,130px))]` — as many 130px columns as
fit, left-packed, leftover space stays empty. A `flex flex-wrap` of
`w-[130px]` tiles is equally acceptable. Either way:

- The poster must **not** grow past the cap at any viewport width — this is a
  cap, not a `flex-grow` basis. (Movie tiles do stretch in width, but their
  196px height is fixed; height is what makes the size read as constant, and
  it's the height that must be bounded here.)
- Below the cap, narrow viewports may still shrink tiles to fit — never
  overflow the section horizontally.
- Keep `aspect-ratio: 2/3` via `.poster-image-container`; don't hard-code a
  height that fights it.
- Keep the existing `gap-3`, the caption block, hover behaviour
  (`.poster-card` scale + `.poster-shine`), and the nav-to-detail anchor.

### 2. Drop the crosshair badge from `gameInFocusPosterCard`

Delete the `// Crosshair badge` block (the `absolute top-1.5 left-1.5 z-10`
wrapper and its `Icons.crosshairSmFilled ()` span). Leave the poster image,
the no-cover gamepad fallback, and `DesignSystem.posterShine` untouched.

Do **not** touch `movieToWatchPosterCard` or `movieToWatchFilmstripItem` — the
movie badge stays conditional and stays.

If `Icons.crosshairSmFilled` ends up with no remaining reference in this file,
leave the icon itself alone (`Icons.fs` is shared); just make sure the build
has no unused-open warning.

## Acceptance criteria

- [ ] On the All tab, a game poster's rendered height is capped at the movie
      filmstrip poster height (196px) and does not increase as the browser
      window widens — verified at a narrow (~sm), a mid (~lg, section still
      full-width), and a wide (~xl, section halved) viewport.
- [ ] At a wide viewport a game poster and a "Movies to Watch" poster read as
      the same size side by side.
- [ ] Game posters keep their 2/3 aspect ratio and never overflow the Games
      section horizontally at any width.
- [ ] No crosshair / In Focus badge renders on any card in the All-tab Games
      section.
- [ ] The In Focus crosshair still renders on In Focus movies in the "Movies
      to Watch" row (unchanged).
- [ ] Poster hover (scale + shine) and click-through to the game detail page
      still work.
- [ ] `npm run build` compiles clean and `npm test` stays green.

## Notes

- Section built by `intelligence-encn4` (dropped the velvet-card chrome,
  renamed "Games In Focus" → "Games"); the movie row it should match was built
  by `intelligence-p9m4t` on top of the filmstrip sizing from
  `design-system-wd5zk`.
- The badge removal follows `intelligence-f6cfv`'s reasoning verbatim: where
  In Focus is already carried by *which section the item is in*, the badge is
  redundant.
- The Books column next to Games is still `booksColumnPlaceholder` — out of
  scope; don't restructure the `xl:grid-cols-2` split itself.
