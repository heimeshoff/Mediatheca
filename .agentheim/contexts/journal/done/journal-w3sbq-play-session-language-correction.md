---
id: journal-w3sbq
title: Correct Journal's README to the first-class play-session event model — and to the read-model owners that actually exist, since there is no JournalProjection.fs
status: done
type: chore
context: journal
created: 2026-08-01
completed: 2026-08-02
depends_on: [games-p6vkz]
blocks: []
tags: [journal, play-session, documentation]
related_adrs: [0002]
related_research: []
prior_art: []
---

## Why

Journal is a conformist read-side context with **no code change at all** in this workstream. Verified:
there is no `JournalProjection.fs`. Its read models are `GameProjection` queries over
`game_play_session` (`GameProjection.fs:853, 870, 902, 912, 919, 935, 947`) plus
`PlaytimeTracker.getDashboardPlaySessions:435` and `getPlaytimeSummary:406`. Journal already reads the
**table**, never the `Game_play_time_set` event.

But its README asserts a `JournalProjection` that does not exist, names `Game_play_time_set` in its
subscription list, and carries an open question that `games-p6vkz` closes.

## What

- Revise the **Play session** ubiquitous-language entry: first-class event, keyed on gaming day,
  carrying a source (`SteamSync | Manual | Imported`). No friends on a session — "played with" is a
  Game-level relationship, not a session-level one.
- Correct the Key events subscription list at line 32 to the four `Play_session_*` events plus
  `Game_status_changed`.
- **Resolve the open question at line 49.**
- Correct the implied `JournalProjection` to name the real read-model owners (`GameProjection` queries
  and `PlaytimeTracker`, plus the new `PlaySessionProjection`).

## Acceptance criteria

- [ ] `grep -c "Game_play_time_set" .agentheim/contexts/journal/README.md` returns 0.
- [ ] `grep -c "JournalProjection" .agentheim/contexts/journal/README.md` returns 0.
- [ ] The Open questions section no longer contains the `Play_session_recorded` question.
- [ ] The Key events list names all four `Play_session_*` events.
- [ ] No `.fs` file is changed by this task.

## Notes

Pure documentation reconciliation, mirroring the shape of `design-system-x7k2p`.

## Outcome

Corrected `.agentheim/contexts/journal/README.md` to the post-`games-p6vkz` reality, verified directly
against the current worktree's code rather than the task's pre-p6vkz snapshot:

- **Play session** ubiquitous-language entry now describes the first-class `Play_session_recorded`
  event (ADR-0050), keyed on gaming day, carrying `Source: SteamSync | Manual` (the actual
  `PlaySessionSource` DU in `src/Shared/Shared.fs` — no `Imported` case exists in code, so the
  entry doesn't claim one). Notes that "played with" is a Game-level relationship
  (`Game_played_with`), not session-level.
- **Aggregates** section now states plainly that there is no dedicated Journal projection module,
  and names the real owners: `GameProjection` (queries over `game_play_session`), `PlaytimeTracker`
  (`getDashboardPlaySessions` / `getPlaytimeSummary`, both delegating to `PlaySessionProjection`),
  and the equivalent Movies/Series projections.
- **Key events** subscription list corrected to the four `Play_session_*` events
  (`Play_session_recorded`, `Play_session_minutes_corrected`, `Play_session_moved`,
  `Play_session_removed`) plus `Game_status_changed`, replacing the retired `Game_play_time_set`
  / `Game_steam_last_played_set` pair.
- **Open questions** — removed the "should Games emit a real event" question; `games-p6vkz` answered
  it.

No `.fs` files touched. Verified acceptance criteria by grep against the README (all counts as
required) and against `src/Server/Games.fs`, `src/Server/GameProjection.fs`,
`src/Server/PlaySessionProjection.fs`, `src/Server/PlaytimeTracker.fs`, and
`src/Shared/Shared.fs` for the current event/read-model shape.
