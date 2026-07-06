---
id: intelligence-h7v2q
title: Dashboard "Next episode" — cinematic hero cards (backdrop + still + progress + watched-with + Jellyfin play)
status: todo
type: feature
context: intelligence
created: 2026-07-06
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, frontend, series, next-up, hero-card, jellyfin]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk]
---

## Why
The dashboard's TV "Next Up" row is currently a strip of small poster cards. It reads
as a catalog, not as an invitation to hit play. The styleguide already ships a
**cinematic hero card** (`DesignSystem.heroCard`, § 4 "TV Next up hero") that is exactly
the right visual language for "here's the next episode, with whom, how far in, press
play" — but the dashboard doesn't use it. Bring the section up to that treatment so the
first thing on the landing page for a series is genuinely *the next episode*, cinematic
and actionable.

## What
On the Dashboard **All tab**, rework the TV Series next-up section so that:

1. The **section header** reads **"Next episode"** (currently "Next Up").
2. Each entry renders as a **cinematic hero card** in the spirit of the styleguide's
   `DesignSystem.heroCard` — a backdrop-filled panel with a bottom overlay — rather than
   a small poster card. Adapt the styleguide card to a repeated, scrollable/grid form
   (the styleguide specimen is a single large card; here it's one per next-up series).
3. Card composition:
   - **Background:** the series **backdrop** (`BackdropRef`), object-cover, filling the card.
   - **Top-right inset:** the **episode still** (`EpisodeStillRef`), a smaller thumbnail
     pinned to the top-right corner.
   - **Bottom darker overlay** (gradient/scrim so text is legible over the backdrop)
     containing, stacked:
     - the **series name** (`Name`, serif/display voice per the hero card),
     - the **episode name** (`S{NextUpSeason}E{NextUpEpisode}: {NextUpTitle}`),
     - the **segmented progress meter** (`DesignSystem.progressSegmented` /
       `progressSegmentedCapped`) indicating episodes watched vs. total —
       `WatchedEpisodeCount` filled of `EpisodeCount`,
     - the **watched-with people** — image **and** name for each friend in
       `WatchWithFriends` (reuse the `friendPill` pattern, which already renders
       `ImageRef` + `Name`; the styleguide hero uses initials-only avatars, but this
       card should show the real friend images + names).
   - **Bottom-right:** the **Jellyfin play button**, shown **only when** the episode is
     available on Jellyfin — i.e. when both `JellyfinServerUrl` and `item.JellyfinEpisodeId`
     are `Some` (reuse the existing `jellyfinPlayUrl` helper + the bottom-right play-button
     overlay pattern already in this file). Absent otherwise.
4. The card as a whole still navigates to the series detail page (as the current poster
   card does) — the Jellyfin play button is a separate action that must not trigger the
   card's navigation (stop propagation / separate anchor, as the existing overlay does).

Scope is the **All tab** section the user described (`seriesNextUpOpenScroller` in
`src/Client/Pages/Dashboard/Views.fs`, rendered by `allTabView`). The Series-tab next-up
scroller is out of scope for this task unless trivially shared.

## Acceptance criteria
- [ ] The All-tab section header text is **"Next episode"** (not "Next Up").
- [ ] Each next-up entry renders as a backdrop-filled cinematic hero card, visually
      consistent with `DesignSystem.heroCard` (backdrop canvas + bottom scrim overlay),
      not the previous small poster card.
- [ ] The series **backdrop** (`BackdropRef`) fills the card background; a sensible
      fallback is used when `BackdropRef` is `None` (e.g. poster, or a neutral panel) so
      the card never renders empty.
- [ ] The **episode still** (`EpisodeStillRef`) appears as a thumbnail inset in the
      **top-right** corner (omitted gracefully when `None`).
- [ ] The bottom overlay shows the **series name** and the **episode name**
      (`SxxExx: title`) legibly over the backdrop.
- [ ] The **segmented progress meter** shows `WatchedEpisodeCount` filled of
      `EpisodeCount`, using the design-system segmented-progress component.
- [ ] Each **watched-with friend** is shown with **both their image and name**
      (empty/absent when `WatchWithFriends` is empty).
- [ ] The **Jellyfin play button** appears in the **bottom-right only** when both
      `JellyfinServerUrl` and `JellyfinEpisodeId` are present; clicking it opens the
      Jellyfin episode URL without navigating the card to series detail.
- [ ] Clicking elsewhere on the card still navigates to the series detail page.
- [ ] `npm run build` compiles clean (Fable + Vite), no type errors, no new warnings.
- [ ] Design-check passes (paper-overlay/token/typography conventions; scrim + serif
      title + mono episode label follow the styleguide).

## Notes
- Data is **fully available already** on `DashboardSeriesNextUp` (`src/Shared/Shared.fs`
  ~L248): `Name`, `BackdropRef`, `EpisodeStillRef`, `NextUpSeason/Episode/Title`,
  `WatchWithFriends: FriendRef list`, `InFocus`, `JellyfinEpisodeId`, `EpisodeCount`,
  `WatchedEpisodeCount`. **No server, projection, event, or API change is expected** —
  this is a pure client presentation task. If you find a genuinely missing field, bounce
  the task rather than silently extending the projection.
- Building blocks already in `src/Client/Pages/Dashboard/Views.fs`:
  - `jellyfinPlayUrl serverUrl itemId` (L12) and the bottom-right play-button overlay
    pattern (see `seriesPosterCard` ~L320 and `seriesTabPosterCard` ~L3150).
  - `friendPill friend` (L201) — renders `ImageRef` + `Name`, exactly the watched-with
    treatment wanted here.
  - The current section to replace: `seriesNextUpOpenScroller` (~L1050), wired in
    `allTabView` (~L1846) as the first row.
- Styleguide reference: `DesignSystem.heroCard` / `HeroCardProps` (`DesignSystem.fs`
  ~L579) and its `velvetCardHero` canvas + `progressSegmented`. Consider generalizing the
  hero card into a reusable `DesignSystem` component that takes real backdrop/still image
  refs and a friend list (the current specimen hardcodes a gradient + initials); prefer
  extending the design system over a one-off card in the page, per the frontend gate.
- Prior art: **intelligence-dq8rk** built the current 3a All-tab layout and this exact
  `seriesNextUpOpenScroller`; this task upgrades that section's card treatment.
- Frontend gate (design-system-001) is **done** — this task is clear to promote/work.
