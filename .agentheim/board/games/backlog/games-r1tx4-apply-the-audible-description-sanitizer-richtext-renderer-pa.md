---
id: games-r1tx4
title: Apply the Audible description sanitizer + RichText renderer pattern to game descriptions (Steam/RAWG)
status: backlog
type: chore
context: games
created: 2026-09-17
completed:
depends_on: [design-system-001]
blocks: [games-fffvm]
tags: [games, steam, rawg, description, detail-page, frontend, integration, metadata-cache]
related_adrs: [0043, 0045]
related_research: []
prior_art: [books-nvnyk, games-v4nqe]
---

## Why

`books-nvnyk` fixed the same defect for books. Game descriptions still go through a `<[^>]+>` → `""` regex (`stripHtmlTags`, three private copies: `Api.fs`, `PlaytimeTracker.fs`, `Rawg.fs`), which drops `<p>`/`<br>` *without inserting a newline* — so a Steam `about_the_game` lands in `game_metadata_cache.description` as one wall of text, and the game detail page renders it as a single `Html.p` with `prop.text`. Noted as out of scope in books-nvnyk's own Notes ("Follow-up candidate (not captured): apply the same sanitizer + component to game descriptions").

Refinement (2026-09-17) found a second, latent defect on the RAWG side: since games-v4nqe dropped the `game_detail.description` projection column, `addGame`'s RAWG path (`Api.fs`, the `addGame` member) puts RAWG's `description_raw` **only** into the `Game_added_to_library` payload — which `GameProjection.handleEvent` deliberately ignores — and never calls `MetadataCache.upsertGameIdentityCard`. A RAWG-added game therefore has an **empty** description unless the best-effort Steam auto-attach (`attachSteamToGameCore`, "only if the current one is empty") happens to fill it. This task closes that gap on the same creation-path pattern the Steam sites already use.

## What

Mirror books-nvnyk's shape in the games BC — one server-side sanitizer, one client renderer, no event, no projection-handler involvement (cache tier, ADR-0043/ADR-0045):

1. **One shared sanitizer.** Lift `Audible.sanitizeDescription` (allowlist `p`/`br`/`b`/`strong`/`i`/`em`/`ul`/`ol`/`li`, attributes dropped, `<br/>` normalized, every other tag unwrapped to its text content — never deleted) into a shared server module compiled ahead of `Audible.fs`/`Steam.fs`/`Rawg.fs` (e.g. `src/Server/DescriptionSanitizer.fs`), and point Audible at it. One implementation, three adapters. The existing `Audible.sanitizeDescription (books-nvnyk)` Expecto list must keep passing unchanged (an alias is fine).

2. **Steam: sanitize at decode time, collapse the seven copies.** `Steam.fs`'s store-details decoder runs `about_the_game` and `detailed_description` through the sanitizer, so every caller receives sanitized values. The seven identical `if details.AboutTheGame <> "" then stripHtmlTags … elif details.DetailedDescription …` blocks (`Api.fs` ×6 in `runSteamFamilyImport` ×2, `attachSteamToGameCore`, `addGameFromSteamCore`, the Steam library import ×2; `PlaytimeTracker.fs` ×1) collapse into one `Steam.storeDescription : SteamStoreDetails -> string` helper (about-the-game first, detailed-description fallback, `""` otherwise). `short_description` stays plain text (Steam sends it tagless) and is not sanitized. The `FullReenrich` site in `runSteamFamilyImport` computes `desc` but only writes `ShortDescription` — **preserve that behaviour as-is** (out of scope to change; note it in the Outcome if it looks wrong).

3. **RAWG: write the identity card on add.** In `addGame`'s RAWG path, after `Add_game` succeeds and before the Steam auto-attach, call `MetadataCache.upsertGameIdentityCard conn slug { Description = <sanitized>; ShortDescription = ""; WebsiteUrl = None }` — the same imperative creation-path write the Steam sites do (ADR-0045's hard constraint: never the ProjectionHandler). `<sanitized>` is RAWG's HTML `description` field (already decoded into `RawgGameDetailsResponse.Description`, today unused) run through the sanitizer, falling back to `description_raw`, then `request.Description`. The `Game_added_to_library` payload keeps carrying the plain `description_raw` exactly as today — no HTML enters an event. `Rawg.previewGame` (the search-hover preview, rendered via `truncateText 300` + `prop.text` in `SearchModal.fs`) keeps plain `description_raw`; it just stops using a private regex copy.

4. **Delete the three `stripHtmlTags` copies** (`Api.fs`, `PlaytimeTracker.fs`, `Rawg.fs`).

5. **Client.** The game detail page's Description section (`GameDetail/Views.fs`, the `sectionHeader "Description"` block) renders `game.Description` **and** `game.ShortDescription` via `RichText.render` instead of `Html.p`/`prop.text`. The short/full toggle (`IsDescriptionExpanded`, `Toggle_description_expanded`, "Read more…"/"Show less") is unchanged. `RichText.render` already handles legacy plain-text rows (paragraphs on blank lines) and decodes entities, so nothing needs a backfill to keep rendering — legacy flattened rows simply stay one paragraph until `games-fffvm` re-fetches them.

## Acceptance criteria

- [ ] Exactly one sanitizer implementation exists on the server; `Audible.fs`, `Steam.fs` and `Rawg.fs` all use it, and the existing `Audible.sanitizeDescription (books-nvnyk)` Expecto cases pass unchanged.
- [ ] `Steam.getSteamStoreDetails` returns `AboutTheGame`/`DetailedDescription` already sanitized; a new Expecto case feeds a Steam-shaped fixture (`<h2 class="bb_tag">`, `<img src=…>`, `<br>`, `<strong>`, `<ul class="bb_ul"><li>`, `<a href=…>`) through `addGameFromSteam` and asserts the `game_metadata_cache.description` row keeps `<p>`/`<br>`/`<strong>`/`<ul>`/`<li>`, carries no attribute, no `<h2>`, no `<img>`, no `<a>`, and still contains the `<h2>`/`<a>` text.
- [ ] A single `Steam.storeDescription` helper replaces all seven about-the-game/detailed-description selection blocks; `grep -rn stripHtmlTags src/Server` returns nothing.
- [ ] `addGame` with a `RawgId` writes an identity-card row via `MetadataCache.upsertGameIdentityCard` whose `description` is the sanitized RAWG `description` (fallback `description_raw`, then `request.Description`); a new Expecto case stubs the RAWG details call the same way `AddGameFromSteamTests.fs` stubs Steam and asserts the row. The `Game_added_to_library` payload's `description` stays plain `description_raw`.
- [ ] `GameDetail/Views.fs` renders both `game.Description` and `game.ShortDescription` through `RichText.render`; `grep -n "prop.text game.Description\|prop.text game.ShortDescription" src/Client/Pages/GameDetail/Views.fs` returns nothing; the expand/collapse toggle still dispatches `Toggle_description_expanded`.
- [ ] `RichText.test.fs` gains one Steam-shaped case (an `<h2>`/`<img>`/`bb_ul` fixture parses to the expected allowlisted node tree).
- [ ] No event type, no projection column, no `GameProjection.handleEvent` arm changes (`git diff --stat` touches none of `Games.fs`'s event DU / `GameProjection.fs`'s schema).
- [ ] `npm run build`, `npm test`, `npm run test:client` green.
- [ ] On a freshly added Steam game's detail page, the description reads as paragraphs and bullet lists rather than one run of text. [human-eye]

## Notes

- **Site inventory (2026-09-17, `main` @ f2d1aaf).** `Api.fs`: `stripHtmlTags` def L13; Steam selection blocks at ~L783 (`runSteamFamilyImport`, `FullReenrich` branch — writes ShortDescription only), ~L856 (`runSteamFamilyImport`, new-game branch → `upsertGameIdentityCard` ~L916), ~L1379 (`attachSteamToGameCore` → ~L1397), ~L1472 (`addGameFromSteamCore` → ~L1519), ~L4477 (Steam library import, new game → ~L4544), ~L4580 (same import's "backfill empty descriptions" loop → `updateGameIdentityCache`). `PlaytimeTracker.fs`: def L12, block ~L271 (scheduled Steam sync's new-game path → ~L317). `Rawg.fs`: def L410, used only by `previewGame` L441. `addGame` member ~L3552; its RAWG details fetch ~L3594 picks `d.DescriptionRaw` and has **no** cache write.
- **Why decode-time for Steam** (vs. wrapping each call site): Audible already sanitizes in its decoders (`decodeProduct`/`decodeLibraryItem`/`Audnexus.decodeAudnexusBook`); the same placement makes an unsanitized Steam string unrepresentable downstream and is what lets the seven blocks collapse.
- **Steam markup to expect** in `about_the_game`: `<h2 class="bb_tag">`, `<img class="bb_img" src=…>`, `<br>` runs, `<strong>`, `<i>`, `<ul class="bb_ul"><li>`, `<a href=…>` and occasionally `<video>`/`<iframe>`. Under the allowlist: `h2` unwraps to its text (a heading becomes a text run — acceptable, matches books), `img`/`video` unwrap to nothing (no text content), `a` keeps its label. RAWG's `description` is `<p>…</p>` with `<br />` — the tame case.
- **Legacy rows** already in `game_metadata_cache` are `stripHtmlTags` output: tagless, newline-less. `RichText.render`'s plain-text path renders them as one paragraph — unchanged from today, no regression. Re-fetching them so they gain formatting is `games-fffvm` (backlog, `depends_on` this task); RAWG-only games with empty descriptions since games-v4nqe are also that task's to fill.
- **Entities:** Steam/RAWG send `&quot;`/`&amp;`; the sanitizer leaves entities alone and `RichText` decodes them client-side (same as books). Routing `ShortDescription` through `RichText.render` is what fixes a literal `&amp;` in today's short description.
- **Frontend gate:** `depends_on: [design-system-001]` (done) per the games README's styleguide rule; no new design tokens — `RichText`'s own `text-base-content/70 leading-relaxed` voice matches the current description paragraph class.
- `RichText.fs`/`RichText.parse`/`RichText.render` already exist (books-nvnyk) and are generic over any allowlisted-HTML string — no game-specific rendering logic needed.
- Cache tier per ADR-0043/ADR-0045 — no event, no projection-handler involvement, same as books.
- Fixtures only, never the live DB (project standing rule).
