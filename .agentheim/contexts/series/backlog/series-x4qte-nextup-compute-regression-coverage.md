---
id: series-x4qte
title: Add client-side regression coverage for `NextUp.compute` — the frontier rule (gaps behind the furthest-watched episode are history, not a queue) currently has no client test, only its server-side mirror
status: backlog
type: chore
context: series
created: 2026-08-07
completed:
depends_on: [infrastructure-j7v3c]
blocks: []
tags: [testing, vitest, fable, client, series]
related_adrs: [0063]
related_research: []
prior_art: [series-k4zpn]
---

## Why

`series-k4zpn` extracted `NextUp.compute` (`src/Client/Pages/SeriesDetail/NextUp.fs`) as a pure,
Feliz-free `SeasonDto list -> (int * EpisodeDto) option` *specifically* so the frontier rule could
be asserted without driving the DOM — and so the series-detail hero card and the Episodes-tab
"NEXT" badge, which both call it, are mechanically guaranteed to agree.

It shipped with no client-side test. ADR-0063's "Client test coverage deferred" section records
why (no Vitest infrastructure, and standing it up needs an `npm install` that is unsafe from a
worker worktree) and backlogs this task by name. Its correctness today rests on mirroring the
`series_next_up` SQL view function-for-function plus 4 Expecto tests proving the identical rule
*server-side* — real evidence, but not coverage of the client function that actually renders.

The harness half of this task moved to `infrastructure-j7v3c`; what remains here is the series
domain assertion, which is where the frontier rule's language belongs.

## What

Write `NextUp.test.fs` alongside `NextUp.fs`, registered as an explicit `<Compile Include>` item
in `Client.fsproj` immediately after `NextUp.fs` (and before `State.fs`), following the harness
conventions established by `infrastructure-j7v3c`.

The rule under test, from `NextUp.fs`'s own docblock: Next Up is the first episode by
`(season, episode)` order that is unwatched **and strictly past the frontier**, where the frontier
is the maximum `(SeasonNumber, EpisodeNumber)` among watched episodes. A skipped episode behind
the frontier must not pin Next Up forever.

## Acceptance criteria

- [ ] `NextUp.test.fs` covers all five documented behaviours:
      a gap behind the frontier is skipped (the result names the episode *after* the frontier,
      not the gap); no watched episodes anywhere → the first episode overall; nothing exists past
      the frontier → `None`, even with unwatched gaps sitting behind it; a cross-season frontier
      (S1E10 watched, S2E1 unwatched → S2E1) confirming lexicographic tuple ordering; an empty
      `seasons` list → `None` without throwing.
- [ ] `npm run test:client` is green with these specs included.
- [ ] `npm run build` still passes clean.

## Notes

- The cross-season case is the one worth being deliberate about: F#'s structural tuple comparison
  is already lexicographic, unlike SQLite's — that asymmetry is called out in `NextUp.fs`'s
  docblock and is exactly the kind of thing a refactor could silently break.
- Watch-record scope is whatever the caller's `SeasonDto list` carries in `EpisodeDto.IsWatched`
  (for the series-detail page, the *selected rewatch session*'s scope — deliberately not unified
  with the server view's union-across-rewatches scope). Fixtures should set `IsWatched` directly
  and not attempt to model rewatch scoping; that is a separate modelling question per ADR-0063.
- Blocked on `infrastructure-j7v3c` for the harness itself. Do not bootstrap Vitest here.
