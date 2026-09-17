---
id: games-r1tx4
title: Apply the Audible description sanitizer + RichText renderer pattern to game descriptions (Steam/RAWG)
status: done
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

## Verifier note (iteration 1)

REASONS:
- README_DELTA mismatch (check 5). The single `append` op to the games README's "Ubiquitous language" section asserts: "The event payload itself keeps carrying the plain, unsanitized `description_raw` — no HTML ever rides an event (ADR-0043)." The diff falsifies the generalized half of that claim. Because Steam is now sanitized at decode time (`src/Server/Steam.fs:205-206`), `Steam.storeDescription` returns sanitized HTML, and that value flows straight into `Games.GameAddedData.Description` — i.e. the `Game_added_to_library` event payload — at three Steam creation paths: `src/Server/Api.fs:864` (`runSteamFamilyImport` new-game branch, via `Api.fs:847`), `src/Server/Api.fs:1466` (`addGameFromSteamCore`, via `Api.fs:1456`), and `src/Server/Api.fs:4502` (Steam library import new-game branch, via `Api.fs:4483`). Before this diff all three wrote `stripHtmlTags` output — plain text — into the same event field.
- The same fact is misreported in the `## Outcome` (check 5's "body must match what the diff introduced"): the Outcome states the `Game_added_to_library` payload is untouched only for the RAWG path (true and test-covered at `src/Server/Api.fs:3619`), and is silent about the three Steam paths now appending `<p>`/`<br>`/`<strong>`/`<ul>`/`<li>` markup permanently into the append-only event log. No acceptance criterion, no test, and no ADR covers that change — `tests/Server.Tests/AddGameFromRawgTests.fs`'s "The Game_added_to_library event payload keeps the plain description_raw" case pins only the RAWG side.
- The wrong invariant is load-bearing for the next task in this chain: `games-fffvm` (this task's declared `blocks:`) is the follow-up that re-fetches and re-sanitizes exactly these description rows, and would be written against a README statement about event payloads that no longer holds for Steam-created games.

SUGGESTED_FIX: Either keep the stated invariant true — give `Games.GameAddedData.Description` a plain-text value at `Api.fs:864`/`1466`/`4502` (e.g. a `Steam.storePlainDescription` sibling, or strip the allowlisted subset on the event path only) while the `MetadataCache.upsertGameIdentityCard` writes keep the sanitized HTML — or accept the change and correct the README_DELTA plus `## Outcome` to scope the "plain payload" sentence to the RAWG path and say explicitly that Steam creation-path `Game_added_to_library` events now carry the sanitized HTML subset; either way pin the chosen shape with an Expecto case in `AddGameFromSteamTests.fs` mirroring the RAWG one.

ITERATION_HINT: likely-fixable

## Outcome

Applied the books-nvnyk sanitizer + `RichText` renderer pattern to game descriptions, and closed the RAWG identity-card gap `games-v4nqe` left open — across two iterations.

**Sanitizer.** `src/Server/DescriptionSanitizer.fs` is the one shared HTML-subset sanitizer (`sanitize`: allowlist `p`/`br`/`b`/`strong`/`i`/`em`/`ul`/`ol`/`li`, attributes dropped, every other tag unwrapped to text), compiled ahead of `Audible.fs`/`Steam.fs`/`Rawg.fs` in `Server.fsproj`. `Audible.sanitizeDescription` is now an alias onto it (`Audible.fs`); its own Expecto list passes unchanged.

**Steam.** `Steam.fs`'s `decodeStoreData` sanitizes `about_the_game`/`detailed_description` at decode time, so every caller gets sanitized values already; a single `Steam.storeDescription` helper (about-the-game first, detailed-description fallback) replaced all seven duplicated selection blocks across `Api.fs` and `PlaytimeTracker.fs`. `grep -rn stripHtmlTags src/Server` returns nothing — all three private copies (`Api.fs`, `PlaytimeTracker.fs`, `Rawg.fs`) are deleted.

**RAWG.** `addGame`'s RAWG path now calls `MetadataCache.upsertGameIdentityCard` after `Add_game` succeeds, writing RAWG's sanitized HTML `description` (fallback `description_raw`, then `request.Description`) into the identity card — closing the latent empty-description defect the task's Why section named.

**Event-payload invariant (verifier iteration 2 fix).** The verifier's first pass found the sanitized Steam HTML was flowing straight into the `Game_added_to_library` event payload at three Steam creation sites (`runSteamFamilyImport`'s new-game branch, `addGameFromSteamCore`, the Steam library import's new-game branch) plus a fourth the conductor flagged (`PlaytimeTracker`'s scheduled-sync new-game path) — all four build the same `Games.GameAddedData` record. Kept the invariant true rather than relaxing it: added `DescriptionSanitizer.toPlainText` (projects an already-sanitized string down to tagless text — `<br>`/`</p>`/`</li>` become a single newline, every other allowed tag is dropped without inserting whitespace, repeat newlines collapse, result is trimmed) and, at each of those four sites, pass the plain-text projection into `GameAddedData.Description` while the identity-card cache write on the same path keeps the sanitized HTML (`description`/`Steam.storeDescription details` unchanged). The RAWG path already had this split correct (test-covered at `Api.fs`'s `addGame`). Net result: **on every creation path — Steam and RAWG alike — the `Game_added_to_library` event payload's `Description` carries plain text; only the `game_metadata_cache` identity-card tier ever carries the sanitized HTML subset.** The "backfill empty descriptions" loop and `attachSteamToGameCore` still correctly write only the cache (no event) — unchanged. `runSteamFamilyImport`'s `FullReenrich` branch (computes `desc`, writes only `ShortDescription` to the cache) is unchanged, out of scope per the task's own Notes.

**Client.** `GameDetail/Views.fs`'s Description section renders both `game.Description` and `game.ShortDescription` through `RichText.render`; the expand/collapse toggle (`Toggle_description_expanded`) is untouched. `RichText.test.fs` gained a Steam-shaped `<h2>`/`<img>`/`bb_ul` fixture case.

**Tests.** `AddGameFromSteamTests.fs` gained: the sanitizer-at-the-cache-tier case from iteration 1 (`<h2>`/`<img>`/`<a>`/`bb_ul` fixture, asserts `game_metadata_cache.description` keeps the allowlisted subset and drops everything else), a new case mirroring `AddGameFromRawgTests.fs`'s event-payload case (decodes the stored `Game_added_to_library` event's JSON `Data`, asserts its `description` field contains no `<` at all while text content survives, and that `GameProjection.getBySlug`'s cache-backed `Description` still keeps `<strong>`), and a new `DescriptionSanitizer.toPlainText` unit `testList` (three cases: tag/newline behaviour, an already-plain string passes through trimmed, empty stays empty). `AddGameFromRawgTests.fs` (iteration 1) already pins the RAWG-side identity-card write and its own plain-event-payload case.

**Gates (iteration 2, run from the worktree):** `npm run build` — clean Fable client build. `npm test` — Expecto 928/928 passed (up from iteration 1's 924; +4 new cases). `npm run test:client` — Vitest 117/117 passed (unchanged from iteration 1, client-side work untouched this iteration).

Key files: `src/Server/DescriptionSanitizer.fs`, `src/Server/Steam.fs`, `src/Server/Api.fs`, `src/Server/PlaytimeTracker.fs`, `src/Server/Audible.fs`, `src/Server/Rawg.fs`, `src/Client/Pages/GameDetail/Views.fs`, `src/Client/Components/RichText.test.fs`, `tests/Server.Tests/AddGameFromSteamTests.fs`, `tests/Server.Tests/AddGameFromRawgTests.fs`.
