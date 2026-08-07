---
id: design-system-mz9v7
title: Season-rail + per-episode progress primitives in DesignSystem — `progressSeasons` (one line per season, gold when touched, brown when untouched) and `progressEpisodes` (one segment per episode of a single season, driven by a per-episode watched flag instead of a fill count), with StyleGuide specimens
status: doing
type: feature
context: design-system
created: 2026-08-07
completed:
depends_on: []
blocks: [series-ww1rb]
tags: [ui, progress, series, styleguide]
related_adrs: [0015]
related_research: []
prior_art: [design-system-k9p3v]
---

## Why

`DesignSystem.progressSegmented filled total` (`src/Client/DesignSystem.fs:314`) renders
`total` segments and fills the **first `filled`** of them. That is structurally wrong for
episode progress: a user who has watched episodes 1-3 and 6-7 of a ten-episode season sees
five gold segments at the front, not gold-gold-gold-brown-brown-gold-gold. The primitive can
only ever paint a prefix, because a count is all it is given.

The same primitive is also the reason a long-running series renders an unreadable hairline —
one segment per episode across *every* season of the show. At 120 episodes the row carries no
information at all.

Both problems are fixed at the primitive layer, so every consumer gets the fix at once. This
task owns the primitives and their StyleGuide specimens; [[series-ww1rb]] owns the read-model
data and wires the three consumer surfaces onto them.

## What

Two new progress primitives in `src/Client/DesignSystem.fs`, replacing `progressSegmented`:

```fsharp
/// Segmented ("film-frame") episode progress for a *single* season. One segment
/// per episode, in episode order; `watched.[i]` drives segment i, so a gap in the
/// middle renders as a gap. Replaces the count-based fill, which painted the first
/// N segments regardless of which episodes were actually watched.
let progressEpisodes (watched: bool list) : ReactElement

/// Season rail — one line per season, in season order. Gold when the season has at
/// least one watched episode, brown when untouched. Sits directly above the episode
/// row so the card reads "where am I in the show" over "where am I in this season".
let progressSeasons (touched: bool list) : ReactElement

/// The stacked pair every series card uses: season rail on top, current-season
/// episode segments below, with the design system's own vertical rhythm between them.
let seriesSeasonEpisodeProgress (seasonsTouched: bool list) (currentSeasonWatched: bool list) : ReactElement
```

Colours are the ones already in use and confirmed correct — no new tokens:

- gold / watched → `var(--color-gold)` (today's `.progress-segment-filled`)
- brown / unwatched → `oklch(0.32 0.03 30)` (today's `.progress-segment`)

**Two states only** for a season line: touched (≥1 episode watched, gold) or untouched
(brown). A fully-watched season is *not* visually distinct from a partially-watched one —
that was an explicit call, keeping the rail to two states.

The season rail is visually distinguishable from the episode row (it is a coarser, per-season
mark — e.g. a taller or thicker line, wider gaps), so the two rows never read as one long
dotted line. Exact treatment is the design-system's call; the constraint is that at a glance
a viewer can tell which row is seasons and which is episodes.

`progressSegmented` is **retired**, not kept alongside — its count-based fill is precisely the
bug, and all three call sites (`secondaryMediaCard`, `heroCard`, `nextEpisodeHeroCard`) migrate.
The `heroCard` / `secondaryMediaCard` specimen props change from `ProgressFilled: int` /
`ProgressTotal: int` to the flag lists; `nextEpisodeHeroCard`'s props change the same way and
its live consumers are re-wired by [[series-ww1rb]]. The § 4 "Progress meters, two kinds"
prose in the StyleGuide is updated to describe the segmented meter as flag-driven and to cover
the season rail.

CSS lands in `src/Client/index.css` next to the existing `.progress-segmented` /
`.progress-segment` rules, following the same class-based pattern.

## Acceptance criteria

- [ ] `progressEpisodes [true; true; true; false; false; true; true; false; false; false]` renders exactly ten segments, gold at indices 0,1,2,5,6 and brown at 3,4,7,8,9 — the fill honours the flags, not a prefix count.
- [ ] `progressEpisodes []` renders an empty row without throwing (the "no episode data" case).
- [ ] `progressSeasons [true; true; false]` renders three lines: gold, gold, brown. A fully-watched season and a partially-watched season are the same gold.
- [ ] `seriesSeasonEpisodeProgress` stacks the season rail above the episode row in a single element, with the season rail visually coarser than the episode row.
- [ ] `progressSegmented` no longer exists in `DesignSystem.fs`; `rg "progressSegmented" src/` returns zero hits.
- [ ] `HeroCardProps`, `SecondaryCardProps`, and `NextEpisodeHeroCardProps` carry the flag-list shape (season-touched list + current-season watched list) in place of `ProgressFilled` / `ProgressTotal`.
- [ ] The StyleGuide page renders a specimen of the stacked pair using fixture data that includes a hole in the middle of the season — the gap is visible in the specimen, so the bug cannot silently regress.
- [ ] The StyleGuide's § 4 progress-meter prose describes the segmented meter as flag-driven per-episode-of-one-season, and documents the season rail's two states.
- [ ] `npm run build` succeeds (Fable compiles; no FS0039 from the changed prop records).
- [ ] The season rail and episode row read as two distinct rows at the card sizes actually used on the dashboard, and neither is mistaken for the other. [human-eye]

## Notes

- Consumer wiring lives in [[series-ww1rb]], which `depends_on` this task. That task supplies the read-model fields (`SeasonsTouched`, `CurrentSeasonWatched`, `CurrentSeasonNumber`) that feed these primitives. Keep the signatures above stable — they are the contract between the two tasks.
- `seriesNextUpItemEnhanced` (`src/Client/Pages/Dashboard/Views.fs:2630`) overlays a 4px progress bar on the *poster bottom*, which is far too cramped for a stacked two-row treatment. If a compact variant of `seriesSeasonEpisodeProgress` turns out to be needed for that slot, add it here rather than letting the Series-tab wiring invent a one-off — but only if [[series-ww1rb]] actually hits the wall; do not build it speculatively.
- ADR-0015: the in-app StyleGuide page is the authoritative design-system artifact. The specimen is not optional documentation — it is where this primitive is reviewed.
- Fable frontend tests: `skills/fable-frontend-tests` covers the Vitest-through-vite-plugin-fable path if the flag-mapping deserves a unit test. The list→segment mapping is pure and cheap to cover.
