---
id: 0053
title: Game play facets are cache-derived from Steam; per-field manual overrides stay event-sourced and merge at query time
scope: games
status: accepted
date: 2026-08-04
supersedes: []
superseded_by: []
amends: []
related_tasks: [games-a7dqx]
related_research: []
---

# ADR 0053: Game play facets are cache-derived from Steam; per-field manual overrides stay event-sourced and merge at query time

## Context

`games-a7dqx` retires `Game_play_mode_added`/`Game_play_mode_removed` — 7668 events (43% of the
entire event log) carrying Steam Store category strings, in 302 distinct localized-duplicate
forms. Replacing them is a typed `PlayFacets` record (`Solo`, `CoopCouch`, `CoopOnline`,
`VersusCouch`, `VersusOnline`, `RemotePlayTogether`, `Vr`) derived from Steam's numeric category
ids and written to `game_metadata_cache` — a straightforward application of ADR-0043 (Steam's own
description of how a game plays is a third party's fact, re-fetchable at any time) and ADR-0045
(typed per-BC cache tier).

But Marco's decision 5 requires manual editing to survive: non-Steam games have no Steam facets to
derive, and Steam's own categorization is sometimes wrong. A manual correction is Marco's own
judgment, not re-derivable from any external source — ADR-0043's test puts it squarely on the event
side, the opposite tier from the facets it corrects. ADR-0043 named this half of the split
explicitly ("the play-mode designation itself, if it reflects a user decision, stays an event") but
did not work out the *merge*: how a query composes an event-sourced correction with a cache-sourced
default for the same seven-field concept. That composition — not the tier assignment itself — is
what this ADR records, because it is the shape the next "user corrects a third party's field"
feature (a movie overview, a TMDB rating) will need to copy rather than re-derive.

## Decision

### Two records for one concept, not one nullable one

```fsharp
type PlayFacets = {
    Solo: bool; CoopCouch: bool; CoopOnline: bool
    VersusCouch: bool; VersusOnline: bool; RemotePlayTogether: bool
    Vr: VrSupport  // NoVr | VrSupported | VrOnly
}

type PlayFacetsOverride = {
    Solo: bool option; CoopCouch: bool option; CoopOnline: bool option
    VersusCouch: bool option; VersusOnline: bool option; RemotePlayTogether: bool option
    Vr: VrSupport option
}
```

`PlayFacets` (all fields total) is the cache-derived default, written by `game_metadata_cache` and
never carried by an event. `PlayFacetsOverride` (all fields `Option`) is the aggregate-held,
event-sourced correction: `None` means "not overridden, defer to the cache"; `Some v` — including
`Some false` or `Some NoVr` — is a real, distinct statement that overrules whatever the cache says.
The `Vr` field needs the `Option` for the same reason the six booleans do: `Some NoVr` ("Steam says
VR-supported, I say no") is not expressible if the override type collapses to a bare `VrSupport`.

### One event carries the whole override, not seven

```fsharp
| Game_play_facets_overridden of PlayFacetsOverride   // event
| Override_play_facets of PlayFacetsOverride           // command
```

The facet vocabulary is closed and typed — unlike `Game_family_owner_added`'s open friend-slug set,
which genuinely needs per-element add/remove events, this is the `Set_hltb_hours` shape (several
related fields, set together, no-op-checked by equality). Per-facet events were rejected: they would
reintroduce a stringly-typed facet *name* as a payload value, require a hand-maintained `PlayFacet`
DU kept in sync with the record, and — for a task whose founding complaint is event-log volume —
multiply a rare edit by seven.

"Un-overriding" a facet needs no separate event or command: sending `None` for that field, or
`PlayFacets.noOverride` for all seven, is the same operation as setting an override. This is a
second argument for the full-record shape: per-facet events would need a third state or an explicit
clearing twin to express the same thing.

### The merge is a pure function, computed at query time, never inside a `ProjectionHandler`

```fsharp
let merge (cached: PlayFacets) (ovr: PlayFacetsOverride) : PlayFacets =
    { Solo = ovr.Solo |> Option.defaultValue cached.Solo
      // ...one line per facet, no cleverness — this function IS the ADR-0043
      // doctrine split, executable.
    }
```

Composed in `GameProjection.getAll`/`getBySlug` by joining `game_metadata_cache` (cache tier) to
`game_detail`'s new override columns (projected tier) — ADR-0048's shape (join in the query
function, never the API layer) and ADR-0045's hard constraint (no `ProjectionHandler` ever touches
the cache tier) both apply unchanged; this ADR does not modify either.

### The aggregate stays cache-blind by construction

No invariant refuses an override that happens to match the cache. The Game aggregate has no read
path into `game_metadata_cache` — adding a "this override is redundant" check would require one, and
would be the same CQRS inversion `games-p6vkz` already removed when it pulled the any-status
promotion rule out of a read-model consult and into `decide`. A redundant-but-harmless override is
accepted as normal, self-correcting state.

## Consequences

### Positive
- Gives `games-a7dqx` (and any future BC needing "the third party's fact, correctable by hand")
  a copyable shape: total record = cache default, all-`Option` record = event-sourced override,
  one merge function, composed at query time.
- Keeps the aggregate's invariant surface unchanged (no new invariants beyond "game exists") — the
  override is a value object on an existing aggregate, not a new bounded concept.
- "Manual overrides win over refetch" (decision 5) holds structurally, not by a guard: a refetch
  writes only the cache tier and is incapable of touching the event-sourced override.

### Negative / accepted tradeoff
- `PlayFacetsOverride`'s per-field `Option` is more ceremony than a single `Overridden: bool` flag
  guarding a full `PlayFacets` would have been — rejected because it can't express "override 2 of 7
  facets, defer to Steam on the rest," which decision 5's "~7 typed toggles" implies is exactly the
  expected editing granularity.
- The client must send the override record, not the merged one, or a single toggle flip silently
  freezes all seven facets as explicit overrides — a correctness trap with no compiler-enforced
  guard, flagged here so the implementing worker builds the two-field `GameDetail` DTO
  (`PlayFacets` for display, `PlayFacetsOverride` for the next command) deliberately rather than by
  accident.
- The UI's tri-state question (a two-state toggle can't natively express `Some true`/`Some false`/
  `None`) was resolved at refinement time by the builder: per-facet **Auto/On/Off segmented
  controls** (VR: Auto / No VR / Supported / VR only), with Auto displaying the Steam-derived value
  — recorded in `games-a7dqx`'s acceptance criteria, not here, since it's a UX call, not a
  tactical-modeling one.

## Alternatives considered

- **Single nullable `PlayFacets option` override (whole-record override or nothing)** — rejected:
  can't express "override couch co-op, defer to Steam on everything else," which is the realistic
  correction case (Steam usually gets most facets right).
- **Per-facet events (`Game_play_facet_overridden of Facet * bool`)** — rejected: reopens a
  stringly-typed vocabulary in the payload, needs a hand-synced `Facet` DU, and multiplies event
  volume for a task whose whole point is reducing it.
- **A boolean "refuse override matching cache" invariant** — rejected: requires the aggregate to
  read the cache tier, which ADR-0045's hard constraint forbids and which reintroduces exactly the
  read-model-consult-in-`decide` anti-pattern `games-p6vkz` removed.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`
  — the doctrine this ADR fills a gap in (the split, not the merge).
- `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the cache tier
  and its hard constraint (no `ProjectionHandler` touches it) this ADR does not modify.
- `.agentheim/knowledge/decisions/0048-series-reads-composed-from-metadata-cache-at-query-time.md`
  — the query-time-join precedent this ADR follows.
- `.agentheim/contexts/games/backlog/games-a7dqx-game-attribute-metadata-into-cache.md` — the task
  this ADR was minted alongside.
- `src/Server/Games.fs`, `src/Server/GameProjection.fs`, `src/Shared/Shared.fs` — the code this ADR
  will land in.
