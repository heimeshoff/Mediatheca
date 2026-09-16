---
id: intelligence-p7k3x
title: Prune dead DashboardCrossMediaStats / getRecentActivity / RecentActivityItem payload
status: backlog
type: chore
context: intelligence
created: 2026-09-16
depends_on: []
blocks: []
tags: [dead-code, dashboard, cleanup]
related_adrs: []
related_research: []
prior_art: []
---

## Why

`journal-k52j1`'s refinement (2026-09-16) found, and this task's worker independently re-verified by grep, that `DashboardCrossMediaStats` (`src/Shared/Shared.fs:413`, populated at `src/Server/Api.fs:2807-2808` via 14+ feeder queries computed on every All-tab dashboard load) and `getRecentActivity` / `RecentActivityItem` (`src/Shared/Shared.fs:315,1986`, computed at `src/Server/Api.fs:2716-2764`) have **zero references anywhere under `src/Client` or `tests/`** — no client consumer, no test coverage. This mirrors the already-pruned All-tab heatmap/monthly-breakdown payload (`intelligence-h4qk2`) and the `getDailyReadingActivity` deletion (`git show ca464a1:src/Server/BookProjection.fs`) — both were removed end to end once confirmed dead.

Leaving this payload in place means every All-tab dashboard load pays for 14+ feeder queries (`CrossMediaStats`) and a full activity scan (`getRecentActivity`) that no rendered pixel ever reads, and it's a landmine for the next person who assumes the field's presence in `IMediathecaApi` implies a live surface.

## What

- Confirm (re-grep at execution time, in case something changed) that `src/Client` and `tests/` still have no references to `DashboardCrossMediaStats`, any of its field names, `getRecentActivity`, or `RecentActivityItem`.
- Remove `DashboardCrossMediaStats` and its population in `Api.fs`'s dashboard method; remove the `CrossMediaStats` field from whatever payload type embeds it (`Shared.fs:924`).
- Remove `getRecentActivity` from `IMediathecaApi` (`Shared.fs:1986`), its implementation in `Api.fs` (~2716-2764), and the `RecentActivityItem` type (`Shared.fs:315`).
- Follow the same end-to-end removal shape `intelligence-h4qk2` used for the heatmap/monthly-breakdown payload (delete the type, the API method, the server-side query, and the payload embedding — nothing left half-wired).

## Acceptance criteria

- [ ] `grep -rn "DashboardCrossMediaStats\|getRecentActivity\|RecentActivityItem" src/ tests/` returns nothing.
- [ ] `npm run build` succeeds (Fable compiles clean with the type/method gone).
- [ ] `npm test` passes with no removed test coverage for these (there was none to begin with).