---
id: games-r1tx4
title: Apply the Audible description sanitizer + RichText renderer pattern to game descriptions (Steam/RAWG)
status: backlog
type: chore
context: games
created: 2026-09-17
completed:
depends_on: []
blocks: []
tags: [games, steam, rawg, description, detail-page, frontend, integration, metadata-cache]
related_adrs: [0043, 0045]
related_research: []
prior_art: [books-nvnyk]
---

## Why

`books-nvnyk` fixed the same defect for books: Steam's `about_the_game`/`detailed_description` and RAWG descriptions still go through `stripHtmlTags`-shaped flattening (`Api.fs`, `PlaytimeTracker.fs`, `Rawg.fs`), losing paragraph breaks/emphasis, and the game detail page renders whatever string arrives as a single `Html.p` with `prop.text`. Noted as out of scope in books-nvnyk's own "What" section and Notes ("Follow-up candidate (not captured): apply the same sanitizer + component to game descriptions").

## What

Mirror books-nvnyk's shape in the games BC:
- A shared sanitizer (in `Rawg.fs`/`Steam.fs`, or a common helper both call) that allowlists `p`/`br`/`b`/`strong`/`i`/`em`/`ul`/`ol`/`li`, drops all attributes, and unwraps every other tag to its text content — applied to every Steam/RAWG-sourced description before it reaches `game_metadata_cache`.
- Reuse `src/Client/Components/RichText.fs` (`RichText.render`) on the game detail page's description card instead of duplicating the tokenizer/renderer.

## Acceptance criteria

- [ ] Steam's `about_the_game`/`detailed_description` and RAWG's description are sanitized (allowlisted tags kept, attributes dropped, other tags unwrapped to text) before landing in `game_metadata_cache`.
- [ ] The game detail page's description renders via `RichText.render` instead of a single `Html.p`/`prop.text`.
- [ ] Expecto coverage mirroring `AudibleTests.fs`'s sanitizer cases, for both Steam and RAWG fixtures.
- [ ] `npm run build`, `npm test`, `npm run test:client` green.

## Notes

- `RichText.fs`/`RichText.parse`/`RichText.render` already exist (books-nvnyk) and are generic over any allowlisted-HTML description string — no game-specific rendering logic should be needed, only the server-side sanitizer call sites.
- Cache tier per ADR-0043/ADR-0045 — no event, no projection-handler involvement, same as books.