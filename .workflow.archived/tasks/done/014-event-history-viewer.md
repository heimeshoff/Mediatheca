# Task: Event History Viewer on Detail Pages

**ID:** 014
**Milestone:** --
**Size:** Large
**Created:** 2026-02-20
**Dependencies:** None

## Objective
Add an event history viewer to every entity detail page (Movies, Series, Games, Friends, Catalogues). Users can inspect the full event timeline of any entity in a polished modal, accessed via a hover-reveal menu button.

## Details

### Backend

**New API method** on `IMediathecaApi`:
- `getStreamEvents: string -> Async<EventHistoryEntry list>` -- takes a stream ID prefix (e.g. `Movie-inception-2010`), reads events from all matching streams (entity stream + ContentBlocks stream), merges them chronologically, and returns human-readable entries.

**`EventHistoryEntry` shared type:**
```
{ Timestamp: string
  Label: string          // Human-readable event name, e.g. "Added to library", "Status changed to Playing"
  Details: string list }  // Key details extracted from the payload, e.g. ["Rating: 8", "Status: Playing"]
```

**Server-side formatting:**
- Each bounded context (Movies, Series, Games, Friends, Catalogs, ContentBlocks) provides a `formatEvent: StoredEvent -> EventHistoryEntry` function that maps raw DU case names + JSON payloads into human-readable labels and detail lines.
- The API reads events from the entity's stream AND the ContentBlocks stream (for Movies/Games that have journal entries), merges by timestamp ascending.

### Frontend — Shared Components

**`EventHistoryModal` component** (new file in `src/Client/Components/`):
- Glassmorphism modal (follows existing modal patterns).
- Chronological timeline, grouped by date.
- Each event shows: time, icon per event category (status change, rating, added, removed, etc.), human-readable label, and detail lines.
- Pretty design: date group headers, subtle separators, icons, readable typography.

**`ActionMenu` component** (new file in `src/Client/Components/`):
- A hover-reveal button (glass icon or ellipsis) that opens a glassmorphism dropdown menu.
- Menu items: "Event Log" (opens the modal) + destructive actions ("Remove Movie", "Remove Game", etc.).
- Follows the existing glassmorphism dropdown pattern (`.rating-dropdown` / `.glass-card` in `index.css`).

### Frontend — Per-Page Integration

**GameDetail:**
- Place the `ActionMenu` next to the existing "Change backdrop" button in the hero area (top-right, hover-reveal).
- Menu items: "Event Log" and "Remove Game".
- Remove the standalone "Remove Game" button from the sidebar.
- Stream: `Game-{slug}` + `ContentBlocks-{slug}`.

**MovieDetail:**
- Add an `ActionMenu` in the hero area (top-right, hover-reveal, same pattern as Games' "Change backdrop").
- Menu items: "Event Log" and "Remove Movie".
- Remove the standalone "Remove Movie" button from the sidebar.
- Stream: `Movie-{slug}` + `ContentBlocks-{slug}`.

**SeriesDetail:**
- Add an `ActionMenu` in the hero area (top-right, hover-reveal).
- Menu items: "Event Log" and "Remove Series".
- Keep "Abandon/Unabandon" as a separate button (it's a workflow action, not a destructive one).
- Remove the standalone "Remove Series" button from the sidebar.
- Stream: `Series-{slug}`.

**FriendDetail:**
- Replace the trash icon button (top-right of card) with the `ActionMenu`.
- Menu items: "Event Log" and "Delete".
- Stream: `Friend-{slug}`.

**CatalogDetail:**
- Replace the "Delete" button in the header with the `ActionMenu`.
- Menu items: "Event Log" and "Delete".
- Keep the "Edit" button separate.
- Stream: `Catalog-{slug}`.

## Acceptance Criteria

- [x] Every detail page (Movie, Series, Game, Friend, Catalog) has a hover-reveal action menu
- [x] Action menu contains "Event Log" and the destructive action (Remove/Delete)
- [x] "Event Log" opens a glassmorphism modal showing the entity's full event history
- [x] Events are displayed in human-readable form (no raw DU cases or JSON)
- [x] Events are grouped by date with icons per event category
- [x] For Movies and Games, ContentBlocks events are merged into the timeline
- [x] Timestamps are shown for each event
- [x] The standalone Remove/Delete buttons are removed from their old locations
- [x] Modal follows glassmorphism design rules (backdrop-blur, semi-transparent bg, subtle border)
- [x] ActionMenu dropdown follows glassmorphism design rules
- [x] No `backdrop-filter` nesting issues (dropdowns rendered as siblings, not children of blurred parents)

## Work Log

**2026-02-20**: Implemented full event history viewer feature.

### Backend
- Added `EventHistoryEntry` shared type to `Shared.fs` with Timestamp, Label, Details fields
- Added `getStreamEvents` API method to `IMediathecaApi`
- Created `EventFormatting.fs` module with human-readable event formatters for all 6 bounded contexts (Movies, Series, Games, Friends, Catalogs, ContentBlocks)
- Implemented the API endpoint in `Api.fs` that reads entity streams + ContentBlocks streams for Movies/Games, merges chronologically, and returns formatted entries

### Frontend Components
- Created `ActionMenu.fs` with two variants: `view` (standard) and `heroView` (glass-styled for hero sections)
- Created `EventHistoryModal.fs` with self-contained ReactComponent that fetches events via API, groups by date, shows timeline with category-specific icons, glassmorphism modal styling
- Both components use the `.rating-dropdown` CSS class for glassmorphism dropdowns, rendered as siblings to avoid backdrop-filter nesting

### Per-Page Integration
- **GameDetail**: ActionMenu placed next to "Change backdrop" button in hero (hover-reveal). Removed standalone "Remove Game" button from sidebar. Confirm dialog still appears inline when triggered.
- **MovieDetail**: ActionMenu added in hero top-right (hover-reveal). Removed standalone "Remove Movie" button from sidebar.
- **SeriesDetail**: ActionMenu added in hero top-right (hover-reveal). Removed standalone "Remove Series" button from sidebar. Abandon button kept separate.
- **FriendDetail**: Replaced trash icon button with ActionMenu (top-right of card).
- **CatalogDetail**: Replaced standalone "Delete" button with ActionMenu next to "Edit" button.

All 5 pages have `ShowEventHistory` model field, `Open_event_history`/`Close_event_history` messages, and render `EventHistoryModal` when open.

### Build/Test Results
- `npm run build`: Success (Fable + Vite)
- `dotnet build src/Server/Server.fsproj`: Success
- `npm test`: 232 tests pass, 0 failures
