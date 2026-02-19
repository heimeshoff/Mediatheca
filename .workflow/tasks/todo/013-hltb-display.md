# Task: HowLongToBeat display on game detail + dashboard

**ID:** 013
**Milestone:** M4 - HowLongToBeat Integration
**Size:** Small
**Created:** 2026-02-19
**Dependencies:** 012-hltb-api-client

## Objective
Show HowLongToBeat completion time comparison on game detail page and Games dashboard tab.

## Details

### API Integration (src/Server/Api.fs)
- Add an endpoint or integrate into existing flow: when viewing a game detail, if HLTB hours are missing, optionally fetch from HLTB API and store
- OR: add a "Fetch HLTB" button that triggers the lookup on demand
- OR: batch fetch during Steam import or as a background task
- Decision: **on-demand fetch with caching** — first time viewing a game detail, if no HLTB data, offer a "Fetch from HLTB" button. Once fetched, data is stored via `Game_hltb_hours_set` event and cached permanently.

### Game Detail Page (src/Client/Pages/GameDetail/Views.fs)
- Display section showing:
  - Your total play time (already shown)
  - HLTB Main Story average (hours)
  - HLTB Main + Extras average (hours)
  - HLTB Completionist average (hours)
  - Visual comparison: progress bar or percentage (your time / HLTB main story)
- If no HLTB data: show "Fetch from HowLongToBeat" button
- If HLTB search found no results: show "No HLTB data available"

### Games Dashboard Tab (task 010)
- In the Recently Played section, show a small completion indicator per game if HLTB data is available
- E.g., "12h / 50h (24%)" next to the play time

### Shared Types
- Consider expanding `GameDetail` to include `HltbMainHours`, `HltbPlusHours`, `HltbCompletionistHours` (or keep single `HltbHours` as main story only — simpler)

## Acceptance Criteria
- [ ] Game detail page shows HLTB completion times when available
- [ ] Visual comparison between play time and HLTB average
- [ ] "Fetch from HLTB" button when data is missing
- [ ] Graceful handling when HLTB has no data for a game
- [ ] Games dashboard tab shows completion indicator where available

## Notes
- Keep it simple for v1 — main story hours comparison is the core value
- The extra categories (main+extras, completionist) are nice-to-have
- The "Fetch from HLTB" button approach avoids needing background jobs

## Work Log
<!-- Appended by /work during execution -->
