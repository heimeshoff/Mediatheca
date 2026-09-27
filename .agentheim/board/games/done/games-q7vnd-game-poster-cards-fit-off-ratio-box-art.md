---
id: games-q7vnd
title: Game poster cards show off-ratio box art whole — NES/SNES (and any clearly non-2:3) covers fit inside the 2:3 frame with soft bands above and below, like books-r8cfn does for book covers; true 2:3 covers (Steam) keep today's fill
status: done
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

## Verifier note (iteration 1)

**REASONS:**
- Acceptance criterion 2 is not met. It asks for a test with "a portrait-but-wide NES-like input (→ contain)". The test in `src/Client/Components/PosterFit.test.fs` named "a portrait-but-wide NES-like box (e.g. 900x900, square) switches to contain" feeds a square (ratio 1.0), which is not portrait. A real portrait NES box is ~0.71–0.73 wide-to-high (US NES box ≈ 5×7 in, 0.714). With `tolerance = 0.10` in `src/Client/Components/PosterFit.fs` the "cover" band runs 0.600–0.733, so a genuine NES box falls inside it: `decide` returns `Fit.Cover` and the NES art is still side-cropped (660x900 = 0.733 → Cover per the worker's own boundary test; 512x712 = 0.719 would be Cover too). The doc comment claiming a portrait-but-wide NES box lands outside the band is wrong.
- Everything else checks out: build + test:client exit 0 (20 files, 143 tests); scope clean (no `.agentheim/`, no `.poster-card`/`.poster-image-container` transform edits, movie/series `<img>`s untouched); shared `.poster-image--contain` reused not redefined; the React re-render concern is not a real defect (className prop value never changes). Smaller gap: the `Fit.Cover` branch of `onImageLoad` never removes a previously added `poster-image--contain`, so an `<img>` node that later loads a 2:3 src keeps `contain` — rare with fixed refs, worth handling.

**SUGGESTED_FIX:** Narrow the named tolerance (≈5% gives a cover band of ~0.633–0.700; Steam 600x900 = 0.667 exactly) so real NES boxes (~0.71–0.73) switch to `Contain`. Change the NES test to a real portrait-but-wide input such as 512x712, keep a separate square case if wanted, and fix the boundary tests and doc comment to match. Optionally make the `Fit.Cover` branch of `onImageLoad` remove `poster-image--contain`.

**ITERATION_HINT:** likely-fixable

## Verifier note (iteration 2)

**REASONS:**
- The OUTCOME block misdescribes the diff: its third paragraph says `PosterCard.view`/`viewForRoute` attach `onLoad PosterFit.onImageLoad`, but only `viewForRoute` does, and only when `routePrefix = "games"` (`PosterCard.fs` ~91-95). `PosterCard.view` is unchanged — correctly, since it hardcodes the `"movies"` route and never renders a game. Iteration 1's Outcome described this correctly; iteration 2's (which becomes the durable record) does not.
- The code passes every other check: iteration-1 finding fixed (`tolerance = 0.05`, band 0.633–0.700; NES test 512x712 → Contain; separate square case; boundaries 700x1000/701x1000 recomputed; `Fit.Cover` strips a stale `poster-image--contain`); build + test:client exit 0 (20 files, 144 tests, PosterFit 7/7); scope clean; shared `.poster-image--contain` reused; no ADR/README needed.

**SUGGESTED_FIX:** Only the OUTCOME block needs changing, not the code: say only `PosterCard.viewForRoute`, and only for `routePrefix = "games"`, wires `PosterFit.onImageLoad`; the movies-only `PosterCard.view` is deliberately untouched because it never renders a game.

**ITERATION_HINT:** likely-fixable

## Outcome

Game poster cards decide their `object-fit` per loaded cover instead of always cropping to fill. A new pure module, `src/Client/Components/PosterFit.fs`, exposes `decide (naturalWidth: float) (naturalHeight: float) : Fit` (`Fit.Cover | Fit.Contain`), comparing the image's natural aspect ratio against the 2:3 poster frame's `targetRatio` (`2.0/3.0`) with a named `tolerance` of **5%** (cover band 0.633-0.700). This value was chosen (and iterated on, see below) because the task's suggested "10%" band was wide enough to swallow genuine NES box art (~0.71-0.73, a US NES box is ~5x7in = 0.714) as still "close enough to 2:3", which defeated the task's purpose; 5% keeps Steam's exact 600x900 (0.667) library art on `Cover` while pushing real NES ratios, SNES landscape boxes, and square RomM thumbnails to `Contain`. Malformed (zero/negative) natural dimensions fall back to `Cover`.

`PosterFit.onImageLoad` wires this to a real `<img>`'s `load` event: on `Fit.Contain` it adds the shared `poster-image--contain` modifier class (the same class/composition books-r8cfn's book covers use for whole-cover soft letterboxing — reused here, not redefined); on `Fit.Cover` it now also removes that class if a previous decision had added it, so an `<img>` DOM node that gets reused for a different src (fixed refs) never gets stuck with a stale `contain`.

`PosterCard.viewForRoute` (`src/Client/Components/PosterCard.fs`), and only for `routePrefix = "games"`, wires `PosterFit.onImageLoad` onto its `<img>`; the movies-only `PosterCard.view` is deliberately untouched because it never renders a game (it hardcodes the `"movies"` route). Also wired into the Dashboard's `gameInFocusPosterCard`, `gameRecentlyPlayedPosterCard`, `gameRecentlyAddedPosterCard`, `gameUpcomingPosterCard` (`src/Client/Pages/Dashboard/Views.fs`), which attach `onLoad PosterFit.onImageLoad` to their game `<img>` elements only — movie and series poster `<img>`s are untouched and keep the default `cover`. The StyleGuide (`src/Client/Pages/StyleGuide/Views.fs`) gained a poster specimen demonstrating the `poster-image--contain` letterbox treatment shared by games and books. `Client.fsproj` includes the new `PosterFit.fs`/`PosterFit.test.fs` files in the compile order.

Unit coverage lives in `src/Client/Components/PosterFit.test.fs` (Fable.Mocha via Vitest, 7 cases): a true 2:3 Steam cover (600x900 -> Cover), a landscape SNES-like box (1280x920 -> Contain), a genuine portrait-but-wide NES-like box (512x712, ratio ~0.719 -> Contain — this is the case iteration 1's verifier flagged as using a square 900x900 input instead of a real portrait-wide ratio, and that at the old 10% tolerance would have wrongly stayed Cover), a separate square-cover case (900x900 -> Contain), the tolerance's exact upper boundary (700x1000 = 0.700 -> Cover) and just past it (701x1000 = 0.701 -> Contain), and malformed zero dimensions (-> Cover, no divide-by-zero). Verified by hand-computation that with the old 10% tolerance (band 0.600-0.733) the 512x712 NES case would incorrectly land on Cover, and with the new 5% tolerance (band 0.633-0.700) it correctly lands on Contain — confirming the test only passes because of the narrower, corrected tolerance, not despite it. Also verified ordinary near-2:3 portrait covers (500x750, 680x1000, 600x920) all still resolve to Cover under the 5% band, so the narrowing doesn't over-trigger on typical Steam/RAWG portrait art.

`npm run build` and `npm run test:client` both pass (20 test files, 144 tests, up from 143 in iteration 1 with the added square-cover case).

Iteration 2 addressed the verifier's iteration-1 finding: the NES test previously fed a square (900x900, ratio 1.0) input mislabeled as "portrait-but-wide", and the then-10% tolerance would have kept a real NES ratio (~0.71-0.73) on `Cover`, contradicting the acceptance criterion and the doc comment. The tolerance is now 5%, the doc comment on `PosterFit.tolerance` explains the choice and cites the old/new bands, the boundary tests were recomputed for the new 5% edges, and `onImageLoad`'s `Fit.Cover` branch now strips a stale `poster-image--contain` class. Iteration 3 made no code changes; it corrects this Outcome's third paragraph, which iteration 2 had wrongly worded to say both `PosterCard.view` and `viewForRoute` wire `PosterFit.onImageLoad` — only `viewForRoute`, and only when `routePrefix = "games"`, does; `PosterCard.view` is unchanged.
