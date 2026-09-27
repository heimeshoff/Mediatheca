---
id: games-fbf3j
title: Game detail page — show a game's entire play history in the Play History card instead of only the 10 most recent sessions
status: todo
type: bug
context: games
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [game-detail, play-session, frontend]
related_adrs: [0050]
related_research: []
prior_art: [games-p6vkz, games-t69rb]
---

## Why

The Play History card on a game's detail page only lists the 10 most recent play
sessions. Everything older is invisible, even though the sessions exist and are
already sent to the client. The builder wants to see the whole history.

## What

Remove the client-side cap so the Play History card renders every play session the
server returns for the game (`getGamePlaySessions`), newest first, inline in the
card (the page just grows). The cap is `model.PlaySessions |> List.truncate 10` in
`src/Client/Pages/GameDetail/Views.fs` (~line 1317). The server query already returns
all sessions, so no server or API change is expected. Confirm that while working.

Inline add/edit/delete of sessions keeps working for every row, including rows past
the old 10th position.

## Acceptance criteria

- [ ] The Play History card renders one row per play session returned by `getGamePlaySessions`, with no count limit (no `List.truncate`/`take` on the session list in the view).
- [ ] Sessions keep their existing order (newest first).
- [ ] Editing or deleting a session that was beyond the old 10th position works exactly like editing the first rows.
- [ ] `npm run build` succeeds.
- [ ] A game with more than 10 sessions shows all of them on the detail page. [human-eye]

## Notes

- Builder chose "show all, inline" over a "show all" toggle or a scrollable card (2026-09-27).
- Sibling capture: games-zex36 (friends on play sessions) touches the same card. The two tasks are independent, but expect a trivial merge if they run in the same batch.
