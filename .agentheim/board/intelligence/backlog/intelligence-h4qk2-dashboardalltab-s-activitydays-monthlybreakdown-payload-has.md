---
id: intelligence-h4qk2
title: DashboardAllTab's ActivityDays/MonthlyBreakdown payload has no client consumer — either build the heatmap or stop computing it
status: backlog
type: chore
context: intelligence
created: 2026-09-16
completed:
depends_on: []
blocks: []
tags: [dashboard, dead-code, heatmap]
related_adrs: []
related_research: []
prior_art: [intelligence-p4t7k]
---

## Why

While wiring `DashboardActivityDay.Reading` for `intelligence-dnv2y`, a grep of `src/Client` for
`ActivityDays`, `MonthlyBreakdown`, and `heatmap` (case-insensitive) turned up zero matches outside
`src/Server/Api.fs` and `src/Shared/Shared.fs`. `getDashboardAllTab` computes a full 365-day activity
heatmap (`MovieSessions`/`EpisodesWatched`/`GameSessions`/`Reading` per day) and a 12-month breakdown
on every All-tab load, and no page renders any of it. This is exactly the shape `intelligence-p4t7k`
already pruned once for `DashboardAllTab.NewGames` — a payload the client silently stopped reading
after a UI change, left standing in the server computation and the wire contract.

## What

Either:
1. Build the heatmap/monthly-breakdown UI the payload was evidently intended for (a natural home:
   the Journal BC once it exists, or a dedicated Dashboard section), or
2. If no near-term plan exists, prune `ActivityDays`/`MonthlyBreakdown` (and the now four
   `Reading`/`MovieSessions`/`EpisodesWatched`/`GameSessions`/month fields feeding them) from
   `DashboardAllTab`, `Api.getDashboardAllTab`, and every `getDaily*Activity`/`getMonthly*Minutes`
   query that exists only to feed them — mirroring `intelligence-p4t7k`'s end-to-end prune of
   `DashboardNewGames`.

This is a judgment call for the user/refiner, not a worker decision — hence backlog, not a
prescribed fix.

## Acceptance criteria

- [ ] Either a heatmap/monthly-breakdown view renders `DashboardAllTab.ActivityDays`/
      `MonthlyBreakdown` somewhere in the client, or both fields (and their now-unused server-side
      sources) are removed end to end.
- [ ] `npm run build`, `npm test` green.