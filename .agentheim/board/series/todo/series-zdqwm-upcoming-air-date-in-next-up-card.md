---
id: series-zdqwm
title: Series detail hero — move the upcoming air date ("Next episode airs …" / "Returns …") out of the genre/status/rating row and into the Next Up card, which now also shows when caught up and on mobile
status: todo
type: feature
context: series
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [series, ui, hero, next-up, air-date]
related_adrs: []
related_research: []
prior_art: [series-k4zpn, series-x4qte]
---

## Why
On the series detail hero, the upcoming air date currently sits inline in the badge row, between the
status badge and the rating (`src/Client/Pages/SeriesDetail/Views.fs`, the `NextEpisodeAirDate` /
`NextSeasonAirDate` match right after `statusBadge series.Status`). It crowds that row and is
disconnected from the thing it's actually about: what you'll watch next. The Next Up card
(bottom-right of the hero) is the natural home for "what's coming and when".

## What
- **Remove** the next-air-date indicator from the genre/status/rating row entirely — both variants
  ("Next episode airs <date> (<countdown>)" and the season fallback "Returns <date> (<countdown>)").
  That row keeps genres, status badge, and `HeroRating` only.
- **Add** the air-date line to the Next Up card, same wording and same precedence as today
  (episode date preferred, season date as fallback; `formatDateOnly` + `countdownLabel`).
- **Card visibility widens:** the card renders when *either* `NextUp.compute series.Seasons` is `Some`
  *or* an upcoming air date is known (`NextEpisodeAirDate` or `NextSeasonAirDate`).
  - Next Up present + air date → episode line (Season N, Episode M + name) plus the air-date line.
  - Next Up present, no air date → exactly as today.
  - No Next Up (caught up) + air date → the card still shows under the "Next Up" label, with a
    caught-up line (e.g. "All caught up") and the air-date line.
  - Neither → no card (as today).
- **Mobile:** the card is no longer desktop-only (`hidden lg:block` goes). On small screens it renders
  below the title/meta block in the hero; desktop placement (bottom-right of hero) is unchanged.
- Nothing server-side changes — `SeriesDetail` already carries both air-date fields.

## Acceptance criteria
- [ ] The genre/status/rating row of the series detail hero contains no air-date text for any series
      (neither "Next episode airs" nor "Returns").
- [ ] For a series with a Next Up episode and a known `NextEpisodeAirDate`, the Next Up card shows the
      episode (season/episode/name) and "Next episode airs <date> (<countdown>)".
- [ ] For a series with only `NextSeasonAirDate`, the card shows "Returns <date> (<countdown>)".
- [ ] For a caught-up series (no Next Up) with a known air date, the Next Up card still renders, with a
      caught-up line and the air-date line.
- [ ] For a series with neither Next Up nor an air date, no card renders.
- [ ] The card-content decision (which lines to show for a given Next Up / air-date combination) is a
      pure function covered by client unit tests (`npm run test:client`, Fable.Mocha per ADR-0064),
      one case per combination above.
- [ ] The card is visible at mobile width (below `lg`) as well as on desktop.
- [ ] `npm run build` succeeds.
- [ ] The card reads well with the extra line on both desktop and mobile — no awkward wrapping or
      crowding against the hero art. [human-eye]

## Notes
- User decisions (2026-09-27 capture): show the card when caught up if an air date exists; move the
  "Returns …" season fallback too; show the card on mobile.
- Next Up semantics are unchanged — still `NextUp.compute` (frontier rule, series-k4zpn). The
  Next Up episode and the upcoming aired episode can be the same episode; that's fine, show both lines.
- Card uses `DesignSystem.velvetCard` (page chrome, not a floating surface) — keep it; don't switch to
  paper overlay. Monospace (`font-mono`) is the convention for dates/countdowns.
- Exact caught-up wording is the worker's call within the styleguide voice; "All caught up" is a fine default.
