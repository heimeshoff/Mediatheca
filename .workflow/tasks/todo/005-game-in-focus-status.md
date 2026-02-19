# Task: Game InFocus status

**ID:** 005
**Milestone:** M1 - In Focus
**Size:** Small
**Created:** 2026-02-19
**Dependencies:** None

## Objective
Games gain an "InFocus" status between Backlog and Playing in the lifecycle.

## Details

### Shared Types (src/Shared/Shared.fs)
- Update `GameStatus` DU: `Backlog | InFocus | Playing | Completed | Abandoned | OnHold`
- Add InFocus to the ordering (for display/filtering purposes)

### Domain (src/Server/Games.fs)
- Update `GameStatus` DU to include `InFocus`
- Update `decide` for `Change_game_status`: InFocus is a valid transition from Backlog (and vice versa), Playing is a valid transition from InFocus
- No auto-clear needed — status transitions are explicit for games

### Serialization (src/Server/GameSerialization.fs)
- Add "InFocus" case to GameStatus serialization/deserialization
- Ensure backward compatibility (existing games without InFocus still deserialize correctly)

### Projection (src/Server/GameProjection.fs)
- No schema change needed — `status` column already stores the string value
- Just ensure "InFocus" is handled in any status-based queries

### Client — Game List (src/Client/Pages/Games/Views.fs)
- Add "In Focus" filter badge alongside Backlog/Playing/Completed/Abandoned/OnHold
- Badge should appear between Backlog and Playing in the filter bar

### Client — Game Detail (src/Client/Pages/GameDetail/Views.fs)
- Add "In Focus" option in the status selector/dropdown
- Position between Backlog and Playing

### Tests
- Test: transition Backlog → InFocus
- Test: transition InFocus → Playing
- Test: serialization round-trip for InFocus status

## Acceptance Criteria
- [ ] `InFocus` added to `GameStatus` DU in both Shared and Server
- [ ] Serialization handles InFocus (including backward compat)
- [ ] Game list filter badges include In Focus
- [ ] Game detail status selector includes In Focus
- [ ] Valid status transitions include InFocus
- [ ] Tests passing

## Notes
- Unlike Movies/Series where In Focus is a toggle flag, for Games it's a first-class status in the lifecycle
- The existing `setGameStatus` API endpoint handles this — no new endpoint needed

## Work Log
<!-- Appended by /work during execution -->
