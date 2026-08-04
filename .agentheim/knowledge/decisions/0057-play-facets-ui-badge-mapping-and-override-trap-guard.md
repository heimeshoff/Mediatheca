---
id: 0057
title: Play facets UI — 4-badge mapping is Solo/Co-op/Versus/Couch-summary, and the ADR-0053 override trap is guarded by pure Shared record-update functions
scope: games
status: accepted
date: 2026-08-04
related_adrs: [0053, 0054]
related_tasks: [games-j6wkr]
---

## Context

`games-j6wkr` closed the last of the three transitional windows games-v4nqe accepted:
the detail page and games list rendered **no play-mode/facet information at all** between
games-v4nqe landing and this task. Two open shape questions needed a decision that the
task's refinement left to the implementing worker:

1. The task's `## What` section named "up to 4 badges — Solo · Co-op · Versus · Couch —
   with the online/couch distinction as a sub-label" but that phrasing is ambiguous about
   what the fourth "Couch" badge represents once online/couch is *also* a sub-label on
   Co-op and Versus — those two readings are literally in tension (see below).
2. ADR-0053 flagged the one-field-override correctness trap as needing a "machine-checkable"
   guard, but the project has no Fable/Vitest client-test infrastructure to test Elmish
   `update` functions directly (`CLAUDE.md`/task Notes confirmed this and offered the
   escape hatch of "structuring the override-construction as a pure, testable function").

## Decision

**Badge mapping** (`Components/PlayFacetsDisplay.fs`'s `facetBadges`): four independent,
non-exclusive predicates over the merged `PlayFacets`, each producing at most one badge —
so the badge *count* is "up to 4" per the task wording, not a fixed set of 4 slots:

- **Solo** — `Solo`.
- **Co-op** — `CoopCouch || CoopOnline`, sub-label "Couch" / "Online" / "Couch + Online".
- **Versus** — `VersusCouch || VersusOnline`, same sub-label shape.
- **Couch** — `CoopCouch || VersusCouch` (fires independently of the Co-op/Versus badges
  above, as a fast-scan "you can play this in the same room" summary).

This resolves the literal-reading ambiguity by keeping all four names from the task's
`## What` section as four real, independently-firing badges, and satisfying "the
online/couch distinction as a sub-label" via the Co-op/Versus sub-labels — accepting that
the standalone Couch badge and the Co-op/Versus sub-labels can both surface "couch"
information for the same game (redundant but not contradictory; a human scanning badges
gets the couch signal whether they read the summary badge or the sub-label). `Vr` and
`RemotePlayTogether` get no top-level list/card badge — they're edited on the detail page's
segmented controls but not summarized on the compact card, which is within the "up to 4"
budget the task set.

**Override trap guard**: rather than inventing client test infrastructure (out of this
task's scope) or leaving the guard as review-only discipline, the one-field-changed
guarantee was pushed into `Shared.fs` as a `PlayFacetsOverride` companion module — seven
`withX : 'v -> PlayFacetsOverride -> PlayFacetsOverride` functions, each a plain record
`with`-update. Because `Shared.fsproj` is referenced by `Server.Tests.fsproj` (unlike
`Client.fsproj`, which pulls in Feliz/React and has no .NET test harness wired up),
these functions get real Expecto coverage (`PlayFacetsOverrideTests.fs`) without touching
Fable/browser concerns at all. `GameDetail/State.fs`'s seven `Override_*` message arms call
these functions exclusively against `GameDetail.PlayFacetsOverride` (never the merged
`PlayFacets`) — the machine-checkable half of the trap lives in Shared, the client-side
discipline of *calling* it correctly is enforced by the small, uniform shape of each
`Override_*` arm (one `PlayFacetsOverride.withX` call, mirrored one-to-one against the DTO
field), reviewable at a glance.

## Alternatives considered

- **Couch = RemotePlayTogether**, badged "Couch" as shorthand for "you can share this
  couch-style game with a remote friend". Rejected: confusing to a human reader — the badge
  text "Couch" on a facet that is definitionally about *not* needing to share a couch reads
  as wrong at a glance, worse than the chosen reading's mild redundancy.
- **Co-op/Versus badges implicitly mean "online only"**, with "Couch" as the couch-only
  badge and no sub-labels needed at all. Rejected: contradicts the task's explicit
  "online/couch distinction as a sub-label" clause, which only makes sense if Co-op/Versus
  themselves need a sub-label to disambiguate.
- **Standing up Vitest+Fable for a real Elmish `update`-function test.** Rejected as
  disproportionate to this task's scope — the task's own Notes offered the pure-function
  escape hatch explicitly for this reason, and a whole new test toolchain is a separate,
  bigger decision that shouldn't be smuggled into a UI-rewrite task.

## Consequences

- `Components/PlayFacetsDisplay.fs` is now the shared badge/control vocabulary for both
  `Pages/Games` and `Pages/GameDetail` — future facet-adjacent UI work (e.g. surfacing `Vr`
  or `RemotePlayTogether` on cards) should extend `facetBadges` there, not fork a second
  badge renderer.
- `Shared.PlayFacetsOverride` sets a precedent: pure, UI-decision-adjacent helper functions
  that need real test coverage but have no natural home in `Server` can live in `Shared.fs`
  as a companion module to their DTO type, tested from `Server.Tests` even though they're
  primarily client-consumed.
- If the live library later shows the 4-badge reading confusing users in practice, revisit
  this ADR rather than silently drifting the mapping — badges are a `[human-eye]`
  acceptance criterion with no compiler or test guard on the *choice* of mapping, only on
  the override-construction trap.
