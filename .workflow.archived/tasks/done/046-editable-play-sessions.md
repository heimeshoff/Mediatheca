# Task: Editable Play Sessions on Game Detail

**ID:** 046
**Milestone:** --
**Size:** Medium
**Created:** 2026-05-01
**Dependencies:** --

## Objective

Make the "Play History" list on the Game Detail page fully editable so the user can correct erroneous Steam-tracked sessions and record sessions that Steam never saw (offline play, non-Steam games).

Three operations on a single play session: **add**, **edit (date + minutes)**, **delete (with confirmation)**. After any change, the game's `TotalPlayTimeMinutes` is recomputed from the sum of its sessions so the header / stats stay correct.

## Background

Current state:
- `game_play_session` table: `id`, `game_slug`, `steam_app_id`, `date`, `minutes_played`, `created_at`, with `UNIQUE(game_slug, date)` (`src/Server/PlaytimeTracker.fs:38`).
- Sessions are written exclusively by Steam sync (`PlaytimeTracker.runSync`) using `INSERT OR IGNORE` (`PlaytimeTracker.fs:82`).
- Steam sync is **delta-based**: `delta = currentPlaytime - lastTotal` and only records when `delta > 0` (`PlaytimeTracker.fs:376`). So manual sessions never conflict with sync — Steam never overwrites or subtracts. This is the desired behavior; preserve it.
- `Play History` is currently rendered read-only in `src/Client/Pages/GameDetail/Views.fs:1444` from `model.PlaySessions: PlaySessionDto list`, fetched via `IMediathecaApi.getGamePlaySessions` (`Shared.fs:1186`).
- The game's `TotalPlayTimeMinutes` is set by Steam sync via `Games.Set_play_time currentPlaytime` (`PlaytimeTracker.fs:397`) — it currently reflects Steam's authoritative total, not the sum of sessions. After this task, manual edits also need to drive it.

Architectural note: play sessions are **not** event-sourced today — they live in a snapshot table written directly. We continue that pattern (no new event types), keeping the change scoped. The only event we still emit is `Set_play_time` to keep `TotalPlayTimeMinutes` accurate on the game projection.

## Details

### Backend

#### 1. CRUD helpers in `src/Server/PlaytimeTracker.fs`

Add three functions next to the existing `recordPlaySession`:

```fsharp
// Add OR merge: if (slug, date) already exists, sum minutes into the existing row.
// Returns the resulting (id, totalMinutesForThatDate) so the caller can show feedback.
let upsertManualPlaySession
    (conn: SqliteConnection)
    (slug: string)
    (date: string)            // "yyyy-MM-dd"
    (minutesPlayed: int)
    : (int64 * int)

// Edit an existing session by id. If newDate collides with another existing
// session for the same game, MERGE (sum minutes into the other row, delete this one).
// Returns Ok () or Error msg (e.g. invalid date, negative minutes, id not found).
let updatePlaySession
    (conn: SqliteConnection)
    (sessionId: int64)
    (newDate: string)
    (newMinutes: int)
    : Result<unit, string>

// Delete by id. No-op if the id doesn't exist (returns Ok ()).
let deletePlaySession
    (conn: SqliteConnection)
    (sessionId: int64)
    : Result<unit, string>

// Recompute SUM(minutes_played) for the game and emit Games.Set_play_time.
// Called from each of the three mutating endpoints after the SQL change commits.
let recomputeAndPublishTotal
    (conn: SqliteConnection)
    (slug: string)
    (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
    : unit
```

Validation rules (enforced server-side):
- `minutesPlayed` must be `> 0` and `<= 24 * 60` (a session can't exceed 24h on a single date).
- `date` must parse as `yyyy-MM-dd` and not be in the future (compare to server local date).
- For manual sessions inserted by the user, the existing `steam_app_id` column is `NOT NULL` — we'll use **`0`** as the sentinel for "manual / not from Steam" (no schema change). Document this with a one-line comment near the insert.

#### 2. `PlaySessionDto` carries the id — `src/Shared/Shared.fs`

The current DTO doesn't expose the row id, which the client needs to identify a session for edit/delete. Extend it:

```fsharp
type PlaySessionDto = {
    Id: int64           // NEW
    GameSlug: string
    Date: string
    MinutesPlayed: int
    Source: PlaySessionSource   // NEW — discriminator
}

and PlaySessionSource =
    | SteamSync
    | Manual
```

`Source` is derived from `steam_app_id`: `0` → `Manual`, otherwise `SteamSync`. This lets the UI badge manual sessions distinctly (small "manual" tag) and is forward-compatible if we ever want to filter or treat them differently in stats.

Update the SELECT in `getPlaySessionsForGame` to include `id` and `steam_app_id`.

#### 3. New API endpoints — `IMediathecaApi`

```fsharp
addManualPlaySession: string * string * int -> Async<Result<PlaySessionDto, string>>
//                    slug,    date,    minutes
updatePlaySession:    int64 * string * int -> Async<Result<PlaySessionDto, string>>
//                    id,    newDate, newMinutes
deletePlaySession:    int64 -> Async<Result<unit, string>>
```

Each endpoint:
1. Validates inputs (date parse, minute range, slug exists).
2. Calls the corresponding `PlaytimeTracker` helper.
3. Calls `recomputeAndPublishTotal` for the affected slug, which emits `Games.Set_play_time <sum>`.
4. Returns the updated DTO (or `unit` for delete).

Wire them into `Api.fs` next to the existing `getGamePlaySessions` endpoint.

### Frontend

#### 4. State & messages — `src/Client/Pages/GameDetail/Types.fs` + `State.fs`

Add to `Model`:

```fsharp
PlaySessionEditState: PlaySessionEditState
PendingDelete: int64 option   // session id awaiting confirmation

and PlaySessionEditState =
    | Idle
    | Adding of draft: PlaySessionDraft
    | Editing of id: int64 * draft: PlaySessionDraft
    | Saving
    | Failed of string

and PlaySessionDraft = {
    Date: string         // yyyy-MM-dd
    MinutesText: string  // raw input — parse on save
}
```

Messages:

```fsharp
| Add_session_clicked
| Edit_session_clicked of PlaySessionDto
| Session_draft_date_changed of string
| Session_draft_minutes_changed of string
| Session_draft_save
| Session_draft_cancel
| Session_save_completed of Result<PlaySessionDto, string>
| Delete_session_requested of int64
| Delete_session_confirmed
| Delete_session_cancelled
| Delete_session_completed of Result<unit, string>
```

Behavior:
- `Add_session_clicked` → `Adding { Date = today; MinutesText = "" }`.
- `Edit_session_clicked s` → `Editing (s.Id, { Date = s.Date; MinutesText = string s.MinutesPlayed })`.
- `Session_draft_save` validates locally (non-empty date, positive int minutes); on success switches to `Saving` and dispatches the corresponding API call.
- `Session_save_completed (Ok dto)` → splice the dto into `model.PlaySessions` (replace by id when editing; insert + re-sort by date desc when adding); reset to `Idle`. Also re-fetch the game so `TotalPlayTimeMinutes` updates in the header — same `Load_game slug` re-dispatch pattern used elsewhere.
- `Delete_session_requested id` → set `PendingDelete = Some id` (this opens the confirm dialog).
- `Delete_session_confirmed` → dispatch API call.
- `Delete_session_completed (Ok ())` → remove the session from `model.PlaySessions`; clear `PendingDelete`; re-dispatch `Load_game`.

#### 5. UI in the Play History card — `src/Client/Pages/GameDetail/Views.fs`

Modify the existing Play History card (~line 1444):
- Card header gains a small "+ Add" icon button (top-right) → dispatches `Add_session_clicked`.
- Each row gains two icon buttons on hover (edit pencil, delete trash). Use the same icon set used elsewhere in the page; keep the row layout intact.
- When a row is in `Editing` state, replace the row's content with two compact inputs: a `<input type="date">` and a numeric minutes input, plus Save / Cancel buttons. Save is disabled until both inputs validate.
- When `Adding`, render the same compact input row at the top of the list.
- Manual sessions get a small muted "manual" badge to the right of the date.
- Render the list condition as `not (List.isEmpty model.PlaySessions) || model.PlaySessionEditState <> Idle` so the card stays visible when the user is adding the first manual session for a game with no Steam history.

#### 6. Delete confirmation dialog

Use a glassmorphic modal/dialog (must follow the CLAUDE.md glassmorphism rules — `/0.55`–`/0.70` opacity, `backdrop-filter: blur(24px) saturate(1.2)`, render as a sibling to any blurred parent to avoid the nested-`backdrop-filter` gotcha).

Content: `"Delete this play session? <date> · <minutes> min"` with `Delete` (destructive style) and `Cancel` buttons. ESC closes; Cancel and ESC both dispatch `Delete_session_cancelled`.

### Tests

Add Expecto tests in `tests/Server.Tests/` covering:
- Adding a manual session for a date with no existing row creates a new row with `steam_app_id = 0`.
- Adding a manual session for a date that already has a session **merges** (existing row's minutes increase by the added amount; no second row created).
- Editing a session to a new date that collides with another session merges into that other row and removes the edited one.
- Deleting a session removes it; deleting a non-existent id is a no-op.
- After each mutating op, the game's `TotalPlayTimeMinutes` matches `SUM(minutes_played)` for that slug.
- Validation: minutes `<= 0`, minutes `> 1440`, malformed date, future date all return `Error`.
- Steam delta sync still works after manual sessions exist for the same game (the snapshot/delta logic only sees Steam totals — it should be unaffected by manual rows). Verify by simulating a sync run with a fixture.

## Acceptance Criteria

- [ ] On a game with existing Steam-tracked sessions, the Play History card shows an "Add" button and edit/delete icons per row
- [ ] Clicking "Add" opens an inline editor with a date picker (default = today) and a minutes input; saving creates a new session and the game's total playtime in the header increases by that amount
- [ ] Adding a manual session for a date that already has a session merges minutes into the existing row (no duplicate row); the UI shows the new combined value
- [ ] Editing a session changes its date and/or minutes; the game's total playtime recomputes correctly
- [ ] Editing a session's date to a date that already has another session merges into that other session and removes the edited one
- [ ] Deleting a session opens a glassmorphic confirmation dialog; confirming removes it and decreases the game's total playtime; cancelling leaves it untouched
- [ ] Manual sessions display a small "manual" badge so the user can tell them apart from Steam-tracked ones
- [ ] On a game with **no** Steam play history, the user can still add a manual session (the card appears once a draft is open or a session exists)
- [ ] Validation errors (negative minutes, > 24h, future date, malformed date) are surfaced inline; the save button stays disabled until inputs are valid
- [ ] After manual edits exist, running a Steam sync still works correctly — Steam never decreases playtime, and the next Steam delta is added on top of the manual sessions
- [ ] `npm run build` succeeds
- [ ] `npm test` passes all tests, including the new ones
- [ ] Design check passes: glassmorphic confirm dialog rendered as a sibling to blurred parents (no nested `backdrop-filter`); inline editor styling matches the design system; icons use the existing icon set

## Notes

- Keep the `steam_app_id = 0` sentinel scoped — only the new `Source` discriminator on the DTO depends on it. If we later add a proper `source` column, the DTO mapping is the only consumer to update.
- Validation duplication (client + server) is intentional: the client gives instant feedback, the server is the source of truth and protects against direct API misuse.
- Recomputing `TotalPlayTimeMinutes` from `SUM(minutes_played)` is the correct invariant once manual sessions exist. After this change, the Steam sync's `Set_play_time currentPlaytime` call (which sets total to Steam's reported total) will conflict with manual sessions — instead, after Steam writes its delta session, also call `recomputeAndPublishTotal` so the projection reflects manual + Steam combined. Update `PlaytimeTracker.runSync` accordingly.
- Future enhancement (out of scope): per-session note field. Date + minutes is sufficient for v1.

## Work Log

### 2026-05-01 12:55 -- Work Completed

**What was done:**
- Extended `PlaySessionDto` with `Id: int64` and `Source: PlaySessionSource` (`SteamSync` | `Manual`); the `Source` is derived from `steam_app_id` (0 = manual sentinel, no schema change).
- Added CRUD helpers and validated public-facing API in `PlaytimeTracker.fs`: `upsertManualPlaySession`, `updatePlaySession` (with merge-on-collision), `deletePlaySession`, `recomputeAndPublishTotal`, plus the `*Api` wrappers that validate inputs (date format, future date, minutes 1-1440), recompute SUM and emit `Games.Set_play_time`.
- Wired three new Fable.Remoting endpoints (`addManualPlaySession`, `updatePlaySession`, `deletePlaySession`) in `IMediathecaApi` and `Api.fs`.
- Updated `runSync` to call `recomputeAndPublishTotal` after writing a Steam delta session, so manual sessions are preserved alongside Steam totals.
- Frontend: added `PlaySessionEditState` (Idle/Adding/Editing/Saving/Failed) and `PendingDelete` to GameDetail `Model`; added 12 messages and update cases in State.fs covering the full add/edit/delete flow with optimistic UI and re-fetch of game total.
- Replaced the read-only Play History card with an editable version: header "+" button, hover-revealed edit/pencil + delete/trash per row, inline date+minutes editor for adding/editing, "manual" badge for non-Steam sessions, and a sibling-rendered glassmorphic delete confirmation modal (per CLAUDE.md backdrop-filter gotcha).
- Added 12 Expecto tests in `tests/Server.Tests/PlaytimeTrackerTests.fs` covering insert, merge-on-add, edit-with-collision-merge, edit-without-collision, delete (incl. no-op), validation errors, total invariant after multi-op, and Steam delta + manual interaction.

**Acceptance criteria status:**
- [x] Play History shows Add button and per-row edit/delete icons -- verified via `Views.fs` markup
- [x] Adding via inline editor (default = today) creates a session and increases total -- covered by `addManualPlaySessionApi` test
- [x] Adding for an existing date merges minutes -- covered by "Adding a manual session for an existing date merges minutes" test
- [x] Editing changes date and/or minutes; total recomputes -- covered by "Editing a session changes date and minutes" test
- [x] Editing to a colliding date merges into the other row -- covered by "Editing a session with a colliding date merges into the other row" test
- [x] Delete opens glassmorphic confirm; confirm removes and decreases total; cancel leaves untouched -- modal is rendered as sibling, dispatches `Delete_session_confirmed`/`Delete_session_cancelled`; backend behavior covered by delete tests
- [x] Manual sessions show "manual" badge -- conditional render on `session.Source = Manual`
- [x] Game with no Steam history can still add a manual session -- card visibility uses `not isEmpty || editState <> EditIdle`
- [x] Validation errors are surfaced inline; Save disabled while invalid -- both client (`validateDraft`) and server (`parseSessionDate`/`validateMinutes`) enforce; `saveDisabled` gates the button
- [x] Steam sync still works with manual sessions present -- "Manual sessions do not interfere with Steam delta tracking" test verifies the snapshot is unaffected and the next delta is added on top of manual sessions
- [x] `npm run build` succeeds -- production build passes (32.86s)
- [x] `npm test` passes all tests -- 245/245 passing (12 new tests added)
- [x] Design check: glassmorphic modal rendered as sibling (no nested `backdrop-filter`); `rating-dropdown` glass class reused; `Icons.edit ()` from existing icon set; trash uses inline svg of the same trash path

**Files changed:**
- `src/Shared/Shared.fs` -- Added `PlaySessionSource` DU, extended `PlaySessionDto` with `Id` + `Source`, declared 3 new API methods
- `src/Server/PlaytimeTracker.fs` -- Added `getPlaySessionById`, `upsertManualPlaySession`, `updatePlaySession`, `deletePlaySession`, `recomputeAndPublishTotal`, validation helpers, `addManualPlaySessionApi`/`updatePlaySessionApi`/`deletePlaySessionApi`; updated SELECT to expose `id` and `steam_app_id`; updated `runSync` to call `recomputeAndPublishTotal`
- `src/Server/Api.fs` -- Wired `addManualPlaySession`, `updatePlaySession`, `deletePlaySession` endpoints
- `src/Client/Pages/GameDetail/Types.fs` -- Added `PlaySessionDraft`, `PlaySessionEditState` types; added `PlaySessionEditState`/`PendingDelete` model fields and 12 messages
- `src/Client/Pages/GameDetail/State.fs` -- Initialized new model fields; implemented update cases for add/edit/delete flow including local validation
- `src/Client/Pages/GameDetail/Views.fs` -- Replaced read-only Play History card with editable version (Add button, hover edit/delete icons, inline draft editor, manual badge); added glassmorphic delete confirmation modal as sibling
- `tests/Server.Tests/PlaytimeTrackerTests.fs` -- New file with 12 Expecto tests
- `tests/Server.Tests/Server.Tests.fsproj` -- Registered new test file
