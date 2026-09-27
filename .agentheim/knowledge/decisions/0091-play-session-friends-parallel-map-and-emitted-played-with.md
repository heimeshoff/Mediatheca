---
id: 0091
title: Play-session friends are a parallel (day -> friend set) map on ActiveGame; adding a friend to a session emits Game_played_with itself as a second decide-time event, never derived in evolve or a read model
scope: games
status: accepted
date: 2026-09-27
supersedes: []
superseded_by: []
amends: []
related_tasks: [games-zex36]
related_adrs: [0050, 0088]
related_research: []
---

# ADR 0091: Play-session friends — parallel map, decide-emitted implication

## Context

The builder wants to attach friends to an individual play session (a game's
per-day session), mirroring Movies' watch-session friend feature
(`Friend_added_to_watch_session`/`Friend_removed_from_watch_session`). Two
genuine design choices arise that Movies' precedent does not settle cleanly,
because Games' play-session identity and existing `PlayedWith` set differ
from Movies' shape:

1. **Where does session-friend state live on `ActiveGame`?** Movies keys a
   watch session on a synthetic `sessionId` and holds `Friends: Set<string>`
   inside its `WatchSessionState` record. Games deliberately has no synthetic
   session id — the natural key `(gameSlug, gamingDay)` is load-bearing
   (ADR-0050's drift-detector argument) — and `PlaySessions: Map<string, int>`
   is read by several existing `decide`/`evolve` arms (Steam sync, RomM sync,
   correct/move/remove, `recomputeTotal`). Changing its shape to carry friends
   inline would touch every one of those call sites for no benefit to them.

2. **Does adding a friend to a session imply the game-level `PlayedWith`
   fact, and if so, how?** The builder fixed the outcome (yes, monotone,
   removal from a session does not retract it) but left open *how* — emitted
   by the aggregate as a second event from the same `decide` call, or derived
   at query/projection time from the union of session friends. Movies'
   closest precedent (`Friend_added_to_watch_session`'s `evolve` also
   stripping the friend from `Want_to_watch_with`) resolves a structurally
   similar problem by folding the side effect into `evolve` — a shape worth
   naming explicitly why this task does not copy.

## Decision

### A parallel map, not a richer `PlaySessions` value

`ActiveGame` gains `PlaySessionFriends: Map<string, Set<string>>`, keyed by
the same gaming day as `PlaySessions`, initialized to `Map.empty`. Two
structural invariants, maintained entirely by `evolve`:

- `PlaySessionFriends`'s keys are always a subset of `PlaySessions`'s keys.
- The map never holds an empty set for a day — an empty result removes the
  key rather than storing `Set.empty`.

`Play_session_moved`'s `evolve` arm merges (unions) the source and
destination days' friend sets, mirroring the existing minutes merge-on-
collision rule exactly. `Play_session_removed`'s arm drops the day's entry
outright. `Play_session_minutes_corrected` and `Play_session_recorded`
(Steam/Manual/RomM) do not touch this map at all — a sync delta merging onto
an existing day changes only that day's minutes, never its friends, which is
what keeps a Steam/RomM re-sync from silently touching a hand-curated friend
list.

### Two new events, decide-emitted implication

```fsharp
Friend_added_to_play_session of day: string * friendSlug: string
Friend_removed_from_play_session of day: string * friendSlug: string
```

`Add_friend_to_play_session`'s `decide` arm is idempotent (already-present
friend is `Ok []`) and, when the friend is new to the session, returns
**both** `Friend_added_to_play_session` and — only if the friend is not
already in `game.PlayedWith` — `Game_played_with`, in one `Ok [...]` list.
This is the same shape `Record_play_session` already uses for its own
conditional promotion event (`[Play_session_recorded d] @ promotionEvents`):
one pure decision reading only the aggregate's own state, no read-model
consultation. `evolve` for each event stays a single-purpose fold; the
implication is visible directly in the event log rather than reconstructed
by every reader.

**Rejected: deriving `PlayedWith` from the union of `PlaySessionFriends`,
either in a projection or at query time.** This was the most obvious
alternative and is explicitly not chosen. It would resurrect the CQRS
inversion class of bug ADR-0050 already spent one task closing (there, a
write decision consulting a read model; here, a derived-membership read
silently disagreeing with the actually-stored `PlayedWith` set depending on
which projection ran last) and it would make the manual `Add_played_with`/
`Remove_played_with` commands ambiguous against a derived value — the
builder's own requirement that manual removal is a real, standing override
(not retracted by any session fact) only holds cleanly if `PlayedWith`
stays an independently event-sourced set, not a view.

**Rejected: folding the implication into `evolve` instead of `decide`**
(Movies' `Want_to_watch_with`-stripping precedent). Rejected here because it
would require `Friend_added_to_play_session`'s `evolve` arm to reach into
`game.PlayedWith` and conditionally mutate a second field from a single
event — an event whose own payload does not say whether that mutation
happened, unlike `Game_played_with`, which is unambiguous by construction.
Two events, one per effect, keeps every arm a single fact with a single
consequence, and keeps the log itself the record of *whether* the
implication fired for a given add (useful for audit/EventHistoryEntry,
useless if silently folded).

### Idempotency does not re-assert the implication

If the friend is already on the session, `decide` returns `Ok []` — no
`Friend_added_to_play_session`, and, deliberately, no re-check of
`PlayedWith` either. Accepted consequence: a user who manually removes a
friend from `PlayedWith` by hand, then re-adds (idempotently) a friend
already on a session, does not get `PlayedWith` re-asserted by that no-op
call. `PlayedWith`'s manual removal is an explicit, standing override; only
a **new** friend-to-session association re-triggers the implication check.

## Consequences

- `PlaySessionProjection.fs` gains a second table,
  `game_play_session_friend (game_slug, date, friend_slug)`, natural PK, no
  synthetic id — same drift-detector reasoning as `game_play_session`
  itself. Handlers for add/remove/move/removed are ordinary idempotent
  SQL (`INSERT OR IGNORE`, `DELETE`, a move-as-copy-then-delete), unlike
  `mergeSession`'s known non-idempotent additive merge — this task does not
  need to inherit or fix that pre-existing issue.
- `GameProjection`'s existing `Game_played_with`/`Game_played_with_removed`
  handling needs no change: the new implied event rides the same arm every
  manual `Add_played_with` already produces.
- A friend's detail page can answer "which days did I play game G with
  friend F" directly from the new table with no aggregate changes beyond
  the two events above.
- `PlaySessionFriends` can, over time, diverge from being a subset of
  `PlayedWith` — a session friend whose corresponding `PlayedWith` entry was
  later removed manually stays on the session. This is accepted, not a bug:
  `PlayedWith`'s manual removal is a deliberate override of history, and
  retroactively editing session friend lists to match it was never
  requested and is out of scope.
- `Play_session_recorded`'s payload and `evolve` arm need no change; the
  merge-on-same-day behavior for Steam/RomM/Manual sessions already ignores
  `PlaySessionFriends` by construction.

## References

- `.agentheim/knowledge/decisions/0050-play-sessions-first-class-events-two-fold-cursor.md` —
  the CQRS-inversion fix this ADR declines to reintroduce, and the natural-key
  argument `PlaySessionFriends` is kept a parallel map to preserve.
- `.agentheim/knowledge/decisions/0088-romm-session-id-cursor-extends-play-session-two-fold.md` —
  the most recent precedent for extending `ActiveGame`'s play-session state
  without disturbing `PlaySessions`' own shape.
- `src/Server/Games.fs`, `src/Server/PlaySessionProjection.fs`,
  `src/Server/GameProjection.fs`, `src/Server/Movies.fs` (reference only, for
  the watch-session friend precedent this ADR partially diverges from).
