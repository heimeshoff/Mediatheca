---
id: games-ev65k
title: Game release dates from Steam — cached for every Steam-linked game, auto-refreshed while unreleased, surfaced on the detail page and list cards, plus an Upcoming section on the Games tab
status: doing
type: feature
context: games
created: 2026-08-07
completed:
depends_on: [design-system-001, games-k3vps]
blocks: []
tags: [steam, cache, metadata, release-date, upcoming]
related_adrs: [0043, 0045, 0053]
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

- [ ] `SteamStoreDetails` carries the raw `release_date` string and `coming_soon` flag decoded from `appdetails`; existing callers compile and behave unchanged.
- [ ] `game_metadata_cache` holds raw string, parsed sortable date, and coming-soon flag for Steam-linked games; written only via cache-write paths and the refresh job — no `*Projection.fs` code references `MetadataCache` (ADR-0045 zero-grep property preserved).
- [ ] No new event type and no change to any existing event's payload — release date is cache-tier only (ADR-0043); `Year` projection values are byte-identical before/after this feature for existing games.
- [ ] The refresh job is resumable and throttled, walks its own cursor column, and its steady-state candidate query excludes games already released with a parsed past date (the set drains itself).
- [ ] Date parsing handles all four Steam shapes — exact date, month-year, bare year, TBA/"Coming soon" — with tests per shape; unparseable input preserves the raw string and yields no parsed date.
- [ ] `GameDetail` exposes the release date and the game detail page renders it; for a coming-soon game the unreleased state is visible on the page.
- [ ] `GameListItem` exposes enough for list cards to show a release hint on unreleased games only; released games' cards render exactly as before.
- [ ] The Games tab shows an Upcoming section listing unreleased games sorted by parsed date ascending, TBA last; the section is absent (not empty-rendered) when no unreleased games exist.
- [ ] End-to-end: importing Steam appId 2121510 (Tenebris Somnia) yields its October 2026 release date on the detail page, an upcoming hint on its list card, and a row in the Upcoming section.
- [ ] The upcoming treatments (detail, badge, Upcoming section) sit naturally in the existing velvet-card / badge visual language. [human-eye]

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
