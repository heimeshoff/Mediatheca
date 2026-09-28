---
id: series-q7vhm
title: Series detail Next Up card — the "Next episode airs …" line names the episode that airs; when it isn't the Next Up episode shown above it (or the series is caught up), the line spells out its season and episode number
status: done
type: feature
context: series
created: 2026-09-28
completed:
depends_on: [design-system-001]
blocks: []
tags: [series, ui, hero, next-up, air-date]
related_adrs: []
related_research: []
prior_art: [series-zdqwm]
---

## Why
Since series-zdqwm, the Next Up card on the series detail hero shows the next episode to watch
("Season 2, Episode 3" + name) and, underneath it, "Next episode airs <date> (<countdown>)". The
air-date line doesn't say *which* episode airs. Often that isn't the one shown above it. For example,
you're three episodes behind and the airing one is S2E8, or you're caught up and the card only says
"All caught up". As written, the line reads as if it describes the episode above, which is misleading.

## What
Keep the change in `src/Client/Pages/SeriesDetail/NextUpCard.fs` (pure, unit-tested). No server or
DTO change is needed: `SeriesDetail.Seasons` already carries every episode's `AirDate`.

- **Identify the airing episode on the client.** When `NextEpisodeAirDate = Some d`, the airing
  episode is the first episode in `series.Seasons`, by season number then episode number, whose
  `AirDate` (date part, via `formatDateOnly`) equals `d`. Several episodes can share the date (a
  season dropping all at once), and the first one is the one that counts. Matching on the server's
  date avoids re-deriving "today" on the client.
- **Same episode as the one shown:** if the airing episode *is* the Next Up episode shown above it
  (same season and episode number), the line refers back to it without repeating the numbers:
  `Airs <date> (<countdown>)`.
- **Different episode, or caught up:** if the airing episode differs from the Next Up episode, or
  there is no Next Up episode ("All caught up"), the line names it:
  `Season N, Episode M airs <date> (<countdown>)`. This uses the card's own "Season N, Episode M"
  wording.
- **Fallbacks stay as they are:**
  - The season-level fallback (`NextSeasonAirDate`) keeps `Returns <date> (<countdown>)`.
  - If no episode in `Seasons` matches the episode air date (a data gap), the line keeps the current
    `Next episode airs <date> (<countdown>)`.
- The countdown suffix works exactly as it does today: omitted when empty.
- `airDateLine` / `decide` get the extra inputs they need, which are the seasons and the Next Up
  episode `decide` already receives. Both desktop and mobile placements keep sharing
  `nextUpCardBody`, so both pick up the change.

## Acceptance criteria
- [ ] Next Up = S2E3 and the airing episode is S2E3 → air line is `Airs <date> (<countdown>)`, with no season or episode number (unit test in `NextUpCard.test.fs`).
- [ ] Next Up = S2E3 and the airing episode is S2E8 → air line is `Season 2, Episode 8 airs <date> (<countdown>)` (unit test).
- [ ] Caught up (no Next Up) and an episode air date known → air line names the season and episode, and the card still shows "All caught up" (unit test).
- [ ] Two episodes share the next air date → the lower season/episode is the one named (unit test).
- [ ] Episode air date known but no matching episode in `Seasons` → falls back to `Next episode airs <date> (<countdown>)` (unit test).
- [ ] Season-level fallback unchanged: `Returns <date> (<countdown>)` (existing tests stay green).
- [ ] `npm run build` and `npm run test:client` pass.
- [ ] On a real series page, the air line reads unambiguously next to the Next Up episode on desktop and mobile. [human-eye]

## Notes
- Code lives in `NextUpCard.airDateLine` (formatting) and `NextUpCard.decide` (card decision). The
  view is `nextUpCardBody` in `src/Client/Pages/SeriesDetail/Views.fs`.
- Server derivation, for reference: `SeriesProjection.getNextEpisodeAirDate` = earliest
  `series_episode_cache.air_date >= today`. The client match on that exact date keeps the two
  consistent.
- Out of scope: the Dashboard's "Returning soon" and "Next episode" surfaces. This task only covers
  the series detail Next Up card.

## Outcome

`NextUpCard.fs` gains `findAiringEpisode` (private): the first episode, ordered
by (season, episode), whose `AirDate` date-part matches the server's
`NextEpisodeAirDate` — several episodes can share a date (a season dropping
at once), and the lowest by (season, episode) order wins. `airDateLine` now
takes `seasons` and the `nextUp` episode alongside the two air-date options
and, on the episode-date branch, picks its wording from `findAiringEpisode`'s
result:
- same episode as the Next Up episode shown above the line -> `Airs <date>
  (<countdown>)`, no numbers repeated
- a different episode, or no Next Up at all (caught up) -> `Season N,
  Episode M airs <date> (<countdown>)`
- no episode in `seasons` matches the air date (a data gap) -> the original
  `Next episode airs <date> (<countdown>)` wording, unchanged
The season-level fallback (`Returns <date> (<countdown>)`) and the
no-date-at-all case are untouched. `decide` threads `seasons` through to
`airDateLine`; `nextUpCardBody`/the desktop and mobile render slots in
`Views.fs` needed no change since they only render `CardContent`, whose
shape is the same as before — only the call site
`NextUpCard.decide series.Seasons (NextUp.compute series.Seasons) ...`
gained the new `seasons` argument.

`NextUpCard.test.fs` keeps the 5 original `series-zdqwm` cases (updated for
the new `seasons` parameter, passed `[]` where irrelevant so the fallback
generic wording is unchanged) and adds 5 new `series-q7vhm` cases covering:
same-episode "Airs" wording, a differing airing episode named explicitly,
caught-up-but-a-named-episode, a shared-date tie broken by lower
season/episode, and the airing-date-with-no-matching-episode fallback — 10
tests total, all green. `npm run build` (Fable/Vite production build) and
`npm run test:client` (Vitest/Fable.Mocha, 22 files / 156 tests) both pass.

The task's last acceptance criterion — "On a real series page, the air line
reads unambiguously next to the Next Up episode on desktop and mobile" — is
marked `[human-eye]` in the task and was not verified in a browser; the unit
tests above exercise the same wording logic that drives both the desktop and
mobile placements (they share `nextUpCardBody`), but a human should glance at
a real series-detail page to confirm the visual result reads as intended.

Key files: `src/Client/Pages/SeriesDetail/NextUpCard.fs`,
`src/Client/Pages/SeriesDetail/NextUpCard.test.fs`,
`src/Client/Pages/SeriesDetail/Views.fs`.
