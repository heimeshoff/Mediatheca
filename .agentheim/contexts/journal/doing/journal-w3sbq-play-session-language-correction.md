---
id: journal-w3sbq
title: Correct Journal's README to the first-class play-session event model — and to the read-model owners that actually exist, since there is no JournalProjection.fs
status: doing
type: chore
context: journal
created: 2026-08-01
completed:
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
