---
id: games-q7vnd
title: Game poster cards show off-ratio box art whole — NES/SNES (and any clearly non-2:3) covers fit inside the 2:3 frame with soft bands above and below, like books-r8cfn does for book covers; true 2:3 covers (Steam) keep today's fill
status: doing
type: bug
context: games
created: 2026-09-28
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, poster, cover, games, romm]
related_adrs: []
related_research: []
prior_art: []
---

## Why
Retro box art does not match the 2:3 poster frame. SNES boxes are landscape and NES boxes are wider than 2:3, so today's `object-fit: cover` crops their sides and cuts off the title and artwork. The builder wants these covers to always show in full, with the same soft treatment above and below that books-r8cfn gives square book covers.

## What
Game poster cards keep the 2:3 frame (`.poster-image-container`) and choose the fit per image:

- **Near-2:3 covers** (Steam's 600×900 library art) keep `object-fit: cover`. Nothing changes for them.
- **Clearly off-ratio covers** (NES/SNES box art from RomM's libretro thumbnails, or anything else whose natural aspect ratio is well away from 2:3) switch to `object-fit: contain`, centred. The space above and below (or at the sides) shows the container's soft background, not an empty hole.

The decision is made on the client when the image loads, from `naturalWidth / naturalHeight` against 2:3, with a tolerance, for example "within about 10% of 0.667 counts as 2:3". The worker picks the exact threshold and records it. No server or event change.

Scope: every game poster card.
- Dashboard: `gameInFocusPosterCard`, `gameRecentlyPlayedPosterCard`, `gameRecentlyAddedPosterCard`, `gameUpcomingPosterCard` in `src/Client/Pages/Dashboard/Views.fs`.
- Any `PosterCard.view` / `PosterCard.viewForRoute` (`src/Client/Components/PosterCard.fs`) that renders a game, e.g. via `EntryList`.
- Movies and series posters are unaffected.

Reuse books-r8cfn's contain modifier, meaning its class or its `DesignSystem` composition for "whole cover, soft letterbox". Do not invent a second one. If this task lands first, create the shared composition here and let books-r8cfn reuse it. Either way, keep one soft-letterbox treatment in `index.css`/`DesignSystem.fs` and add it to the StyleGuide poster specimens.

## Acceptance criteria
- [ ] A game poster whose loaded image is clearly off 2:3 (e.g. a landscape SNES box) renders with `object-fit: contain`. One whose image is near 2:3 (a Steam library cover) still renders with `object-fit: cover`. Checkable via computed style on the `img` in the DOM.
- [ ] The fit rule is a pure function of the image's natural width/height, with a named tolerance, and has a client unit test (Vitest/Fable.Mocha). The test covers a 2:3 input (→ cover), a landscape SNES-like input (→ contain), and a portrait-but-wide NES-like input (→ contain).
- [ ] Movie and series poster images still render with `object-fit: cover`.
- [ ] Every game card in a Dashboard Games rail keeps the same rendered width and height regardless of its cover's shape (DOM `getBoundingClientRect`).
- [ ] Games and books use the same soft-letterbox modifier / `DesignSystem` composition (one definition), and the StyleGuide shows it on a poster specimen.
- [ ] A SNES game's box art (e.g. from RomM) shows in full inside the 2:3 frame, centred, with soft bands above and below that read like the book-cover treatment. [human-eye]
- [ ] `npm run build` passes and `npm run test:client` passes.

## Notes
- Builder decision (2026-09-28): apply the rule to off-ratio images in general, not only RomM-sourced covers. A known consequence: a game added through RAWG takes RAWG's `background_image`, usually a 16:9 screenshot, as its cover (`Rawg.downloadGameImages`). Under this rule that cover also gets letterboxed instead of centre-cropped. This was accepted when choosing the option. If it looks bad, the remedy is games-hm3sf's manual cover upload, not a narrower rule here.
- Sibling: books-r8cfn (todo) does the same for book covers and also fixes book rail widths. Coordinate on the shared contain composition; whichever task lands second reuses the first one's composition.
- Nearby: design-system-k4tw8 (Movies filmstrip hover jitter) touches `.poster-card` / `.poster-image-container` transforms. Don't touch those transforms here.
- Cover sources: Steam `downloadSteamCover` (2:3 library art), RAWG `background_image` (landscape screenshot), RomM `url_cover` (libretro box-art thumbnails, native box ratio).
