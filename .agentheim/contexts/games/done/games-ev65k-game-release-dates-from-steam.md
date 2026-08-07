---
id: games-ev65k
title: Game release dates from Steam — cached for every Steam-linked game, auto-refreshed while unreleased, surfaced on the detail page and list cards, plus an Upcoming section on the Games tab
status: done
type: feature
context: games
created: 2026-08-07
completed: 2026-08-07
depends_on: [design-system-001, games-k3vps]
blocks: []
tags: [steam, cache, metadata, release-date, upcoming]
related_adrs: [0043, 0045, 0053, 0060]
related_research: []
prior_art: [games-b8xnw, games-a7dqx]
---

## Why

Mediatheca cannot track release dates. The builder is about to import an unreleased
game (Tenebris Somnia, Steam appId 2121510, releasing October 2026) via the new
Steam-sourced import (games-k3vps), and today the app would show it as just another
backlog entry with a year — no signal that it isn't out yet, no answer to "when can
I play this?". Steam's `appdetails` API already returns
`release_date: { coming_soon, date }`; the codebase currently strips only a 4-digit
year out of it for search ranking (`Steam.fs` `tryParseReleaseYear`) and throws the
rest away.

## What

Release date becomes cached third-party metadata on every Steam-linked game
(builder decision 2026-08-07: **all** games, not just unreleased ones — released
games gain a precise date beyond their year), auto-refreshed while a game is
unreleased so slipped dates correct themselves, and surfaced in three places
(builder decision, same session): the game detail page, the library list cards
(for unreleased games), and a new **Upcoming** section on the Games tab.

**Tier: cache, not event (ADR-0043).** A release date is a third party's
description of the world, re-derivable from Steam at any time — and for upcoming
games it *changes* (delays are common), which is exactly what the cache tier
absorbs and an event stream shouldn't. No new event carries a release date. The
event-carried `Year` identity card (ADR-0055's reasoning) is untouched — this
feature never rewrites `Year`.

**Shape (follow the games-b8xnw / games-a7dqx precedent):**

- `SteamStoreDetails` decodes `release_date` (the raw display string and the
  `coming_soon` flag) from `appdetails` — additive, existing callers untouched.
- `game_metadata_cache` gains release-date columns: the raw Steam display string,
  a best-effort parsed sortable date, and the coming-soon flag. Written by the
  existing cache-write paths (creation path after `Add_game`, Steam-fetch call
  sites) and by the refresh job below — never by `GameProjection.handleEvent`
  (ADR-0045's hard constraint; b8xnw's iteration-1 failure was exactly this).
- **Refresh job:** a resumable throttled backfill following the
  `GameFacetBackfill`/`GameDeckCompatBackfill` shape with its **own cursor
  column** (b8xnw lesson: a shared cursor lets one job's stamp silently exempt
  another's work). Initial pass covers all Steam-linked games; steady state
  re-fetches only games still unreleased (coming-soon, future-dated, or
  unparseable date), so the polling set drains itself as games release.
- **Fuzzy dates are normal, not an error:** Steam returns exact dates
  ("25 Oct, 2026"), month-year ("October 2026"), bare years ("2026"), and
  "Coming soon"/"To be announced". Keep the raw string for display; parse what's
  parseable into the sortable column; unparseable/TBA sorts last in Upcoming.

**Surfaces:**

- **Detail page** — release date shown in the metadata area; an unreleased game
  makes its upcoming-ness obvious (e.g. the raw date string as an "Upcoming"
  treatment near the year).
- **List cards** — unreleased games carry a compact release hint (e.g. an
  "Oct 2026" badge alongside the existing badge vocabulary in
  `Components/PlayFacetsDisplay.fs`-style placement). Released games' cards are
  unchanged.
- **Games tab** — a new **Upcoming** section (vision: individual tabs "expandable
  over time"): unreleased games sorted soonest-first, TBA/unparseable last.

## Acceptance criteria

- [x] `SteamStoreDetails` carries the raw `release_date` string and `coming_soon` flag decoded from `appdetails`; existing callers compile and behave unchanged.
- [x] `game_metadata_cache` holds raw string, parsed sortable date, and coming-soon flag for Steam-linked games; written only via cache-write paths and the refresh job — no `*Projection.fs` code references `MetadataCache` (ADR-0045 zero-grep property preserved).
- [x] No new event type and no change to any existing event's payload — release date is cache-tier only (ADR-0043); `Year` projection values are byte-identical before/after this feature for existing games.
- [x] The refresh job is resumable and throttled, walks its own cursor column, and its steady-state candidate query excludes games already released with a parsed past date (the set drains itself).
- [x] Date parsing handles all four Steam shapes — exact date, month-year, bare year, TBA/"Coming soon" — with tests per shape; unparseable input preserves the raw string and yields no parsed date.
- [x] `GameDetail` exposes the release date and the game detail page renders it; for a coming-soon game the unreleased state is visible on the page.
- [x] `GameListItem` exposes enough for list cards to show a release hint on unreleased games only; released games' cards render exactly as before.
- [x] The Games tab shows an Upcoming section listing unreleased games sorted by parsed date ascending, TBA last; the section is absent (not empty-rendered) when no unreleased games exist.
- [x] End-to-end: importing Steam appId 2121510 (Tenebris Somnia) yields its October 2026 release date on the detail page, an upcoming hint on its list card, and a row in the Upcoming section.
- [x] The upcoming treatments (detail, badge, Upcoming section) sit naturally in the existing velvet-card / badge visual language. [human-eye]

## Notes

- **depends_on games-k3vps** (completed 2026-08-07 11:43, mid-capture — the
  dependency is already met): it is the import entry path the builder will use
  for Tenebris Somnia, and it landed on the same `Steam.fs`/`Api.fs`
  search-and-import surface this task extends — build on what it shipped.
- **Out of scope:** RAWG's `released` field for non-Steam games — Steam-linked
  games only for now; a RAWG-parity capture can follow if non-Steam upcoming
  games ever matter.
- **Out of scope:** any lifecycle/status interaction — an upcoming game sits in
  whatever status the user gives it (typically Backlog, or InFocus for
  anticipation); release day changes nothing automatically.
- Sorting semantics for partial dates (month-year → which day for sort purposes)
  are the worker's call — pick something stable and test it; display always uses
  the raw string, so the choice is invisible to the user.
- Steam appdetails with `filters=basic,release_date` is already the exact request
  the search-ranking path makes (`Steam.fs` `fetchStoreMeta`) — the full-details
  fetch used by import/backfill paths returns `release_date` without extra filters.
- Decisions on partial-date sorting, the self-draining backfill predicate, and
  the `IsUnreleased` definition recorded in ADR-0060.

## Outcome

Shipped Steam release dates as `game_metadata_cache`-only, cache-tier metadata
(ADR-0043/ADR-0045/ADR-0060) — no event, no override, read straight into
`GameListItem`/`GameDetail` and surfaced on the detail page, list cards
(unreleased games only), and a new Upcoming section on the Games tab.

- **Steam.fs** — `SteamStoreDetails` gains additive `ReleaseDateRaw: string`/
  `ComingSoon: bool`, decoded from `appdetails`'s `release_date` object
  (`decodeReleaseDate`); `Categories`/`CategoryIds`/every existing field
  untouched, existing callers (`PlaytimeTracker.fs`, the description
  backfill, `fetchStoreMeta`'s separate lightweight decoder) compile and
  behave unchanged.
- **ReleaseDateParsing.fs** (new) — pure `tryParse`/`tryParseSortable`
  handling all four Steam shapes: exact date (multiple day/month-order and
  abbreviated/full-month variants), month-year (sorts as the 1st of that
  month), bare year (sorts as 1 January), and TBA/empty/unparseable (`None`,
  raw string preserved elsewhere for display). 11 unit tests, one per shape
  plus the day-of-month convention and the `tryParse`/`tryParseSortable`
  agreement.
- **MetadataCache.fs** — `game_metadata_cache` gains `release_date_raw`,
  `release_date_parsed`, `coming_soon`, and its own
  `release_date_fetched_at` cursor (idempotent `ALTER TABLE ADD COLUMN`,
  same idiom as the facet/deck-compat columns). New
  `upsertGameReleaseDate` (`INSERT ... ON CONFLICT DO UPDATE`, same slice
  discipline as `upsertGameFacets`/`upsertGameDeckCompat`) and
  `findGamesNeedingReleaseDateBackfill` — a steady-state candidate query
  deliberately different from the other two backfills' permanent
  "never fetched" cursor: a row stays a candidate while `coming_soon`,
  unparseable, or future-dated, and only drains out once confirmed released
  with a past parsed date (ADR-0060).
- **GameReleaseDateBackfill.fs** (new) — resumable, throttled (300ms,
  mirrors `GameFacetBackfill.fs`/`GameDeckCompatBackfill.fs`), walks the
  cursor above, parses via `ReleaseDateParsing.tryParseSortable`, writes via
  `upsertGameReleaseDate`. Wired into `Composition.fs`'s scheduled jobs as
  "Game release-date backfill", 07:00 local (an hour clear of the
  Deck-compat backfill).
- **GameProjection.fs** — private `readReleaseDateInfo` computes
  `IsUnreleased` once, server-side (`ComingSoon OR a parsed date still in
  the future` — deliberately not "unparseable implies unreleased", ADR-0060),
  never routed through `MetadataCache` (same local-decode precedent as
  `readDeckCompat`, keeping the ADR-0045 zero-code-reference invariant —
  verified: `grep -rn "MetadataCache" src/Server/*Projection.fs` has zero
  code matches, only pre-existing comments). Wired into `getAll`/
  `getBySlug`/`getRecentlyAddedGames`. New `getUpcomingGames` — unreleased
  Steam-linked games, soonest parsed date first, TBA/unparseable last,
  dismissed games excluded (mirrors `getRecentlyAddedGames`).
- **Shared.fs / Api.fs** — new `ReleaseDateInfo` type (`Raw`/`Parsed`/
  `ComingSoon`/`IsUnreleased`) added to `GameListItem`/`GameDetail`; new
  `getUpcomingGames: unit -> Async<GameListItem list>` endpoint. Every
  creation-path/Steam-fetch call site in `Api.fs` that already writes play
  facets (`updateGameFacetsFromCategoryIds`) now also writes the release
  date via a new `updateGameReleaseDate` helper — 8 call sites total,
  including `addGameFromSteamCore` (the games-k3vps import path the
  Tenebris Somnia end-to-end criterion exercises).
- **Client** — `Components/PlayFacetsDisplay.fs` gains `releaseDateBadge`
  (compact list-card badge, "Oct 2026"-style short label from the parsed
  date or the raw string verbatim for TBA, renders nothing for a released
  game) and `releaseDateHero` (detail-page "Upcoming · {raw}" pill near the
  year). `Pages/Games` loads `UpcomingGames` independently via a new
  `Load_upcoming_games`/`Upcoming_games_loaded` message pair and renders a
  horizontally-scrolling Upcoming section (absent, not empty-rendered, when
  the list is empty) above the main grid; sort/filter logic lives entirely
  server-side (`getUpcomingGames`) so it stays Expecto-tested rather than
  living untested in Fable-only client code.
- **ADR-0060** written: the day-of-month convention for partial dates, the
  self-draining backfill predicate, and the `IsUnreleased` definition —
  three judgment calls the task explicitly left to the worker.

Tests: 29 new (`ReleaseDateParsingTests.fs` — 11 pure parsing tests;
`GameReleaseDateBackfillTests.fs` — 8 tests covering resumability,
the steady-state self-draining predicate, cursor independence from the
facets/deck-compat backfills, and scope discipline, mirroring
`GameDeckCompatBackfillTests.fs`; `GameReleaseDateProjectionTests.fs` — 9
tests covering honest degradation, `IsUnreleased` computation, `getUpcomingGames`'s
filter/sort/dismissed-exclusion, and `checkProjectionDrift` staying zero;
`AddGameFromSteamTests.fs` — 1 new end-to-end test importing Tenebris
Somnia, appId 2121510, asserting the October 2026 release date reaches the
detail page, the list card's `IsUnreleased` flag, and a row in
`getUpcomingGames`). Full suite: 672/672 passing. `npm run build` (Fable
compile gate): green.

The final acceptance criterion (visual cohesion of the badge/hero/section
treatments with the existing velvet-card/badge language) is marked
`[human-eye]` — verified by code review (the badge/pill styling reuses the
existing chip classes `deckCompatBadge`/`HeroRating` already establish) and
the Fable build; visual confirmation in a running browser is deferred to
normal review, the same stance `games-b8xnw`'s Outcome took for its own
badge.
