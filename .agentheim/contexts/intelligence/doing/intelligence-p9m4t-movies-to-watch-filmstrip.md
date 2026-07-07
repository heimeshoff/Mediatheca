---
id: intelligence-p9m4t
title: Dashboard "Movies to Watch" — wrap posters in the filmstrip well
status: doing
type: feature
context: intelligence
created: 2026-07-07
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, movies, filmstrip, design-system]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk, intelligence-h7v2q]
---

## Why
The Dashboard "Movies to Watch" section currently renders as a plain horizontal
poster-card scroller. The reviewed 3A / §4 direction (see "Mediatheca Directions.html"
section 3A, and the live StyleGuide "Movies Filmstrip" specimen) frames the
to-watch films inside a **filmstrip** — a black, sprocket-holed well that reads as a
strip of celluloid. This upgrades the section built by intelligence-dq8rk to match the
directions, the same way intelligence-h7v2q lifted the "Next Up" strip into cinematic
hero cards.

## What
Reskin the Dashboard All-tab "Movies to Watch" section so its posters sit inside the
existing filmstrip well (`.filmstrip` / `DesignSystem.filmstripRow`, built by
design-system-wd5zk) instead of the current bare scroller. The filmstrip already exists
as a **presentation-only** primitive (poster + caption); this task extends it to carry
the interactive affordances the dashboard tiles need, then wires the section to use it.

Two shape decisions (confirmed with the user at capture):

- **Overflow = fill + scroll hybrid.** Posters keep the 3A proportions (~196px tall,
  poster radius). When the films overflow the strip's width, the **whole sprocketed
  well scrolls horizontally as one piece** — the top and bottom sprocket rows scroll
  together with the posters, so it always reads as one continuous strip of film. (Not an
  inner scroller inside a static well, and not a hard truncation to N posters.)
- **Keep all affordances.** Each tile stays interactive, exactly as today's
  `movieToWatchPosterCard`: click navigates to the movie detail page; the InFocus
  crosshair badge shows top-left for in-focus movies; the Jellyfin play button overlays
  bottom-right when the movie has a `JellyfinId` and a Jellyfin server URL is configured.

## Acceptance criteria
- [ ] The All-tab "Movies to Watch" section renders its posters inside the filmstrip
      well — black background, sprocket-hole perforation strip top and bottom
      (`.filmstrip` / `.filmstrip-sprocket`), matching the 3A specimen — replacing the
      current plain `overflow-x-auto` poster-card row.
- [ ] Posters keep 3A proportions (~196px tall, `--radius-poster`) and read as equal
      tiles within the strip.
- [ ] When the movies overflow the strip width, the entire sprocketed well (sprockets +
      posters together) scrolls horizontally as one piece; no separate static frame
      around a moving inner row.
- [ ] Each tile navigates to `/movies/<slug>` on click.
- [ ] In-focus movies show the InFocus crosshair badge (top-left) on their tile.
- [ ] Movies with a `JellyfinId` show the Jellyfin play button (bottom-right) when a
      Jellyfin server URL is configured; clicking it opens Jellyfin and does not trigger
      the tile's navigation (stopPropagation), as today.
- [ ] Captions (title + year) render beneath the strip, per the specimen.
- [ ] The section stays hidden when there are no movies to watch (current behavior).
- [ ] The filmstrip primitive change follows the h7v2q precedent — interactive bits
      (nav target, InFocus flag, a caller-supplied Jellyfin-button slot) are passed in so
      `DesignSystem` stays decoupled from `Icons` / URL helpers. If `filmstripRow`'s
      signature changes, the StyleGuide "Movies Filmstrip" specimen is updated to match.
- [ ] `npm run build` is clean (Fable compiles, no type errors).

## Notes
- Current code: `src/Client/Pages/Dashboard/Views.fs` — `movieToWatchPosterCard` +
  `moviesToWatchPosterSection` (~L405–505). The bare list-row variant `movieToWatchItem`
  below it is kept as reference/fallback and is out of scope.
- Filmstrip primitive: `DesignSystem.filmstripRow` / `FilmstripItem`
  (`src/Client/DesignSystem.fs` ~L492), CSS `.filmstrip` / `.filmstrip-sprocket`
  (`src/Client/index.css` ~L328). Specimen: `src/Client/Pages/StyleGuide/Views.fs` ~L1628.
- Precedent for the caller-supplied interactive slot (decoupling DesignSystem from
  `Icons`/URL helpers): `DesignSystem.nextEpisodeHeroCard` + `seriesNextEpisodeCard`
  from intelligence-h7v2q.
- Data is already present on `DashboardMovieToWatch` (`Slug`, `Name`, `Year`,
  `PosterRef`, `InFocus`, `JellyfinId`) — pure client presentation, no server /
  projection / event / API change expected.
- Open implementation question for the worker: whether the scroll-the-whole-well behavior
  is best achieved by making `.filmstrip` itself the `overflow-x-auto` scroll container
  (so sprockets, which are `repeating-linear-gradient` backgrounds, extend across the
  scrolled content width) vs. a wrapping approach — resolve during implementation.
- Design gate: frontend task in a frontend-bearing BC → `depends_on` the styleguide
  (design-system-001, done). Run the design-check / StyleGuide gate before completion.
