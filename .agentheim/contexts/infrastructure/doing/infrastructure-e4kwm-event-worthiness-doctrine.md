---
id: infrastructure-e4kwm
title: Record the event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description — and amend ADR-0012's retracted justification
status: doing
type: decision
context: infrastructure
created: 2026-08-01
completed:
depends_on: []
blocks: []
tags: [event-sourcing, doctrine, metadata, cache, determinism]
related_adrs: [0002, 0012, 0031, 0039, 0040]
related_research: [tv-series-metadata-fallback-sources]
prior_art: [integration-m4k7p, administration-btvqa]
---

## Why

The drift detector reports 2437 discrepancies, all in SeriesProjection, because TMDB metadata is
written out-of-band into projection tables while the event that accompanies it carries only counts.
The same category exists across Games (7668 `Game_play_mode_added` events carrying Steam Store
category tags — 43% of the entire event log), and latently in Movies (deterministic today only
because nothing refreshes them).

Fixing the mechanism without writing down the rule guarantees recurrence: the next person to build
movie refresh will rebuild `SeriesRefresh.applyToProjection` from scratch, because nothing in
Administration's language tells them not to.

The rule constrains Movies, Series, Games, Integration and Administration alike, and has a positive
half (play sessions **are** domain history) that binds Games directly. Integration and Administration
are both `generic` — a generic BC cannot be the authority that constrains three `core` BCs' event
vocabulary. `infrastructure` is the context map's declared "standing home for tech decisions that
apply to the system as a whole".

## What

Write a global ADR carrying:

**The test.**

> An event records an **observation of the user's own engagement** with a work. A cache records **a
> third party's description** of the work.
>
> Operative form — **re-derivability**: if the fact can be re-fetched from its source at any time
> without loss, it is cache. If the system observed something at a moment that can never be observed
> again, it is an event.

**The second clause (identity card).**

> An externally-sourced field may remain a projection column **only if** it is written exclusively by
> an event that carries it, and never by a refresh path.

This is what keeps `name`, `year`, `poster_ref`/`cover_ref` and `genres` as projection columns —
they ride in every `*_added_to_library` snapshot, so replay reproduces them deterministically. It is
what makes ADR-0038's wipe-first-import recovery yield a browsable library instead of 106 nameless
rows, and keeps `ORDER BY name` non-degenerate.

**Three boundary calls the naive "external = cache" rule gets wrong.**

- A per-day playtime **delta is an event** even though Steam is the source: Steam exposes only
  cumulative totals, so the delta is observed once and never again.
- `Game_steam_app_id_set` **stays an event** even though Steam assigns the value: the *link* is our
  decision, and `PlaytimeTracker.findByName` (`src/Server/PlaytimeTracker.fs:636`) links by fuzzy
  name match, so corrections must be auditable.
- `Game_family_owner_added` **stays an event**: it is dual-sourced (`Api.fs:442-470` Steam family
  import, `Api.fs:3110-3122` explicit UI action) and the stored value is a **Friends-BC slug** reached
  through a user-maintained `steamIdToFriendSlug` map. Steam alone cannot reconstruct it.

**A classification table** for every event type named in the evidence.

**Amend ADR-0012 in place** — do not mark it superseded. Every substantive decision survives (TMDB
authoritative, provenance as a column, `INSERT OR IGNORE` vs `INSERT OR REPLACE`-resets-`source`,
mandatory synthetic season container, injected-effect core, client provider-blindness). Retract
exactly two passages:

1. the justification clause *"justified because metadata is already a rebuildable read-model cache,
   not aggregate state"*;
2. the consequence *"a full projection rebuild drops them and the next sync re-creates them"* —
   that consequence is precisely the defect.

ADR-0039 and ADR-0040 **coexist unchanged**; their subjects (image-file naming, backfill predicate)
are tier-agnostic and only their table names move.

**Update the living docs.**

- `.agentheim/vision.md`, under Design Principles: *"**Replayable**: rebuilding projections from the
  event log always yields the same result. Third-party metadata is cached, not evented."*
- `.agentheim/context-map.md`: Administration's shared-kernel line gains *the metadata cache*;
  Integration's relationship gains **two output channels** (commands for domain facts, cache writes
  for third-party metadata); Games' core language gains **play session**; the Movies/Series/Games →
  Journal edge changes "play-time events" → "play session events".

## Acceptance criteria

- [ ] New ADR exists in `.agentheim/knowledge/decisions/` with `scope: global`, `status: accepted`, and a Decision section containing the test and the identity-card clause verbatim.
- [ ] The ADR is listed under `<!-- adr-global:start -->` in `.agentheim/knowledge/index.md`.
- [ ] `0012-*.md` diff shows exactly the two named passages changed, and `status:` is still `accepted`.
- [ ] A cross-reference exists in both directions between the new ADR and ADR-0012 (amendment relationship, not supersession).
- [ ] `.agentheim/context-map.md` contains the string `metadata cache`, and Administration's shared-kernel bullet names all three of event store, image store, metadata cache.
- [ ] `.agentheim/vision.md` contains the string `Replayable`.
- [ ] No `.fs` file is changed by this task.

## Notes

ADR-0012 is arguably the decision that normalized out-of-band projection writes in this codebase —
its reasoning was "metadata is already a projection-level cache", which was the right instinct
applied to a tier that did not exist yet. This task builds the tier and corrects the reasoning
without discarding the decision.

ADR-0059's convention check does not apply — Mediatheca is a consumer install, not the agentheim
harness (ADR-0059 amendment, `agentic-workflow-z3grd`).
