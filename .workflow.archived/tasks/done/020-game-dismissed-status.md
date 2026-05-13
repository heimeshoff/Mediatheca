# Task: Game "Dismissed" Status

**ID:** 020
**Milestone:** --
**Size:** Small
**Created:** 2026-02-24
**Dependencies:** None

## Objective

Add a `Dismissed` status to the `GameStatus` discriminated union — for games the user has decided they're not interested in (without having started playing). Dismissed games use a `badge-neutral` (solid grey) pill and are hidden from the default game list, only visible when the "Dismissed" filter is explicitly selected.

## Details

### 1. Shared Types (`src/Shared/Shared.fs`)

- Add `| Dismissed` to the `GameStatus` DU (after `OnHold`)

### 2. Server Domain (`src/Server/Games.fs`)

- Add `Dismissed` case to `encodeGameStatus` → `"Dismissed"`
- Add `"Dismissed"` case to `decodeGameStatus` → `Dismissed`
- Thread through any domain validation if present

### 3. Server Projection (`src/Server/GameProjection.fs`)

- Add `Dismissed` case to `encodeGameStatus` → `"Dismissed"`
- Add `"Dismissed"` case to `parseGameStatus` → `Dismissed`

### 4. Event Formatting (`src/Server/EventFormatting.fs`)

- Add `| "Dismissed" -> "Dismissed"` to `formatGameStatus`

### 5. Game Detail Page (`src/Client/Pages/GameDetail/Views.fs`)

- Add `| Dismissed -> "badge-neutral"` to `statusBadgeClass`
- Add `| Dismissed -> "Dismissed"` to `statusLabel`
- Add `Dismissed` to the `allStatuses` list in `HeroStatus`

### 6. Games List Page (`src/Client/Pages/Games/Views.fs`)

- Add `| Dismissed -> "Dismissed"` to `statusLabel`
- Add `| Dismissed -> "text-neutral"` to `statusTextClass`
- Add `Dismissed` to the `allStatuses` list in `statusFilterBadges`
- **Default filter behaviour:** When no filter is selected ("All"), exclude games with `Dismissed` status. Only show dismissed games when the "Dismissed" pill is explicitly active.

### 7. Tests (`tests/Server.Tests/GamesTests.fs`)

- Add `Game_status_changed Dismissed` serialization round-trip test
- Add `Dismissed` to the exhaustive event list test

## Acceptance Criteria

- [x] `Dismissed` appears in the status dropdown on game detail pages
- [x] Dismissed games show a grey `badge-neutral` pill
- [x] The games list hides dismissed games by default (when "All" is selected)
- [x] Selecting the "Dismissed" filter pill shows only dismissed games
- [x] Status change events for Dismissed display correctly in the event history
- [x] All existing tests pass; new serialization round-trip test passes
- [x] No existing game data is affected (existing statuses remain unchanged)

## Work Log

### 2026-02-24 — Implementation complete
- Added `Dismissed` case to `GameStatus` DU in Shared.fs
- Added encode/decode for `Dismissed` in Games.fs (server domain serialization)
- Added encode/parse for `Dismissed` in GameProjection.fs (projection serialization)
- Added `"Dismissed" -> "Dismissed"` to `formatGameStatus` in EventFormatting.fs
- Added `badge-neutral` badge class, `"Dismissed"` label, and `Dismissed` to `allStatuses` in GameDetail/Views.fs
- Added `"Dismissed"` label, `"text-neutral"` text class, and `Dismissed` to `allStatuses` in Games/Views.fs
- Updated game list filter: when "All" is selected (StatusFilter = None), games with Dismissed status are excluded; Dismissed games only shown when explicitly filtered
- Added `Game_status_changed Dismissed` round-trip test and added `Dismissed` to exhaustive event list in GamesTests.fs
- All 233 tests pass; client build succeeds
