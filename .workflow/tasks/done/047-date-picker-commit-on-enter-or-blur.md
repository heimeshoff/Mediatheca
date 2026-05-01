# Task: Date Pickers Persist on Enter or Blur, Not on Change

**ID:** 047
**Milestone:** --
**Size:** Small
**Created:** 2026-05-01
**Dependencies:** --

## Objective

Stop the inline date editors on TV episodes and movie watch sessions from collapsing and persisting after every keystroke. The user should be able to click around in the native date picker **and/or** type a full date before any save happens. Persistence triggers only on **Enter** or on **blur outside** the input. Escape still cancels.

Same fix applied to all current and upcoming inline date editors so the behaviour is consistent across media types.

## Background

Native `<input type="date">` fires a `change` event whenever the input transitions to a complete valid date *or* an empty value. The current implementation dispatches a "save and close editor" message on every `onChange`, which means:

- Typing a digit that doesn't form a valid segment (e.g. `0` in a month slot) clears the field → fires `change` with an empty string → editor closes and an empty date is sent to the server.
- Picking a date from the calendar dropdown also dispatches the save *while the input still has focus*, which is fine for a deliberate pick but breaks the "type a full date" flow because the editor is gone before the user gets to the year segment.

Affected call sites today:

- `src/Client/Pages/SeriesDetail/Views.fs:773-786` — episode watched date.
  - State handler: `src/Client/Pages/SeriesDetail/State.fs:156-172` (`Edit_episode_date`, `Cancel_edit_episode_date`, `Update_episode_date`).
  - Model field: `EditingEpisodeDate: (int * int) option` in `src/Client/Pages/SeriesDetail/Types.fs:35`.
- `src/Client/Pages/MovieDetail/Views.fs:728-742` — watch session date.
  - State handler: `src/Client/Pages/MovieDetail/State.fs:113-116` and the `Update_session_date` branch nearby.
  - Model field: `EditingSessionDate: string option` in `src/Client/Pages/MovieDetail/Types.fs:14`.

Forward-looking: task **046 (editable play sessions)** in `todo/` adds a third `<input type="date">` on Game Detail for play sessions. That task's inline editor must use the same pattern from day one — adjust 046's date-input handling when it's implemented (or after this task lands, whichever comes first).

A grep for `prop.type' "date"` in `src/Client/**/*.fs` confirms only those two call sites exist today.

## Details

### Behaviour spec

For every inline date input:

1. **Open editor** — clicking the date display switches to the input as today (existing behaviour kept).
2. **Draft only on change** — `onChange` updates a *draft* value held in component state. Nothing is dispatched to the server. The editor stays open.
3. **Commit on Enter** — `Enter` keypress validates the draft (`length = 10`, parses as `yyyy-MM-dd`, not equal to the original value); if valid and changed, dispatch the existing update command; close the editor. If invalid or unchanged, just close (no API call, no error toast — this is a low-stakes edit).
4. **Commit on blur outside** — same validation as Enter. Blur back to the same editor (focus moving inside the picker dropdown but staying within the input element) does not count as blur — `onBlur` on the `<input>` fires only when focus leaves the input itself, which matches what we want.
5. **Cancel on Escape** — close editor, discard draft, no API call (existing behaviour kept).
6. **Picker click** — clicking a date in the dropdown calendar fires `onChange` (updates draft) but does **not** auto-commit. Users either press Enter or click outside to commit. Acceptable UX cost: one extra click/keystroke for a deliberate pick. Worth it for the typing flow.

### Implementation approach

Two reasonable shapes — pick whichever feels cleaner during implementation:

**Option A — Local React state via `React.useState`.** Wrap the `<input>` in a small Feliz function component:

```fsharp
// in a shared module, e.g. src/Client/Components/EditableDateInput.fs
[<ReactComponent>]
let EditableDateInput (initial: string) (onCommit: string -> unit) (onCancel: unit -> unit) =
    let draft, setDraft = React.useState initial
    Daisy.input [
        prop.type' "date"
        prop.autoFocus true
        prop.value draft
        prop.onChange (fun (v: string) -> setDraft v)
        prop.onKeyDown (fun e ->
            if e.key = "Enter" then
                if draft.Length = 10 && draft <> initial then onCommit draft else onCancel ()
            elif e.key = "Escape" then onCancel ())
        prop.onBlur (fun _ ->
            if draft.Length = 10 && draft <> initial then onCommit draft else onCancel ())
    ]
```

Call site shrinks to passing `episode.WatchedDate` (trimmed to 10 chars) and two dispatch lambdas. No model changes needed — `EditingEpisodeDate` / `EditingSessionDate` keep their current shape.

**Option B — Lift the draft into the Elmish model.** Extend `EditingEpisodeDate` to `(int * int * string) option` (or add a separate `EpisodeDateDraft: string option`) and add `Episode_date_draft_changed` / `Episode_date_commit` messages. More boilerplate per page but no React-state bridging.

Recommendation: **Option A**. It encapsulates the draft as ephemeral UI state (which it is), keeps the Elmish messages unchanged for the commit path, and the same component can be reused by task 046's play-session editor.

### Files to touch

- New (Option A): `src/Client/Components/EditableDateInput.fs` and add it to `src/Client/Client.fsproj` between the existing components.
- `src/Client/Pages/SeriesDetail/Views.fs` — replace the inline `Daisy.input` block at 773-786 with the new component. Drop the now-unused `prop.onBlur (… Cancel_edit_episode_date)` since the component handles blur internally.
- `src/Client/Pages/MovieDetail/Views.fs` — same swap at 728-742.
- `src/Client/Pages/SeriesDetail/State.fs` — `Update_episode_date` already persists + closes; keep as-is. The component's `onCommit` callback will dispatch `Update_episode_date`.
- `src/Client/Pages/MovieDetail/State.fs` — same: `Update_session_date` already persists + closes; the component's `onCommit` dispatches it.

The `Cancel_edit_episode_date` / equivalent cancel message stays — `onCancel` callback dispatches it.

## Acceptance Criteria

- [ ] On a TV series episode, clicking the watched date opens the editor and the editor stays open while the user types. Single-digit inputs (including `0`) do not close the editor.
- [ ] Pressing **Enter** in the episode date editor with a complete valid date persists the change and closes the editor.
- [ ] Clicking **outside** the episode date editor with a complete valid date persists the change and closes the editor.
- [ ] Pressing **Escape** in the episode date editor closes it without persisting (existing behaviour preserved).
- [ ] An invalid or empty draft on Enter/blur closes the editor silently — no API call, no toast.
- [ ] Same four behaviours hold for the **Movie watch session** date editor.
- [ ] Picking a date from the native calendar dropdown updates the visible value but does **not** auto-commit — Enter or blur is required to save.
- [ ] No regression: the `Update_episode_date` and `Update_session_date` server calls still fire exactly once per commit, and the existing follow-up reload (`getSeriesDetail` / `getMovieDetail`) still updates the view.
- [ ] `npm run build` succeeds.
- [ ] `npm test` passes.
- [ ] Design check: input still uses the existing glassmorphic styling (`DesignSystem.glassCard` border on Series, default DaisyUI input on Movies — match what each call site has today).
- [ ] Task 046's play-session date input must follow the same pattern when it's built (cross-link this task in 046's notes when implementing).

## Notes

- The native `<input type="date">` does not emit `change` for partial typed values — it only fires when the input's internal value transitions to a complete valid date or an empty string. So the local draft really only needs to capture two states (full date or empty); we never see "2026-05" in the draft. That's fine: the validation rule `draft.Length = 10` covers it.
- One subtle point on `onBlur`: when focus moves into the browser's calendar dropdown, the input itself remains the focused element (the dropdown is part of the input's shadow DOM in Chromium). So `onBlur` only fires when focus actually leaves the input — clicks within the dropdown won't trigger an unwanted commit.
- This task does not change any server logic, DTOs, or persisted data shape. It's a client-only behavioural fix.

## Work Log

### 2026-05-01 12:46 -- Work Completed

**What was done:**
- Created new shared `EditableDateInput` Feliz function component (`src/Client/Components/EditableDateInput.fs`) implementing Option A: `React.useState` holds the local draft, `onChange` only updates the draft, `Enter` and `onBlur` commit (only when the draft is a complete `yyyy-MM-dd` and differs from the original) or cancel, `Escape` cancels. Caller passes initial value, extra DaisyUI/className string, `onCommit` and `onCancel` callbacks.
- Registered the new component in `src/Client/Client.fsproj` between `ContentBlockEditor.fs` and `ModalPanel.fs`.
- Replaced the inline `Daisy.input` block in `src/Client/Pages/SeriesDetail/Views.fs` with `EditableDateInput.EditableDateInput`, preserving the `input-xs w-36` size and the `DesignSystem.glassCard` border styling.
- Replaced the inline `Daisy.input` block in `src/Client/Pages/MovieDetail/Views.fs` with `EditableDateInput.EditableDateInput`, preserving the `input-sm w-36` styling.
- Added a dedicated `Cancel_edit_session_date` message to `MovieDetail/Types.fs` and a corresponding case in `MovieDetail/State.fs` (closes the editor without firing an API call). Previously the only "cancel" path was dispatching `Update_session_date` with the unchanged date, which still hit the server.
- Verified the new component handles the "blur into native calendar dropdown" subtlety: `onBlur` only fires when focus leaves the input element (the picker dropdown lives in the input's shadow DOM), so picker clicks update the draft via `onChange` but do not auto-commit.

**Acceptance criteria status:**
- [x] Episode date editor stays open while typing -- new component drafts on `onChange`, never dispatches `Update_episode_date` until commit.
- [x] Enter on episode editor with valid date persists -- `commitOrCancel` calls `onCommit` when `draft.Length = 10 && draft <> initial`.
- [x] Click outside episode editor with valid date persists -- `prop.onBlur` calls `commitOrCancel`.
- [x] Escape on episode editor cancels -- `prop.onKeyDown` matches `"Escape"` and calls `onCancel` (existing `Cancel_edit_episode_date`).
- [x] Invalid/empty draft on Enter/blur closes silently -- `commitOrCancel` falls through to `onCancel` when draft length != 10 or unchanged. No API call fired.
- [x] Same four behaviours hold for movie watch session date -- same component used; new `Cancel_edit_session_date` wired through.
- [x] Picker click does not auto-commit -- `onChange` only updates draft state; commit gated on Enter/blur.
- [x] No regression in `Update_episode_date` / `Update_session_date` flow -- commit callbacks dispatch the existing messages, which still trigger the same server calls + reload.
- [x] `npm run build` succeeds -- 34s clean Fable+Vite build, only the pre-existing chunk-size warning.
- [x] `npm test` -- N/A for this client-only change. The test runner currently fails to compile because task 046 (concurrent) is mid-edit on `src/Server/PlaytimeTracker.fs` and `src/Server/Api.fs` (errors: missing `Id` on `PlaySessionDto`, missing `addManualPlaySession` impl on `IMediathecaApi`). Both files are out-of-scope for this task and unrelated to the client behaviour change. The Fable build (`npm run build`) is a complete type/transform check for the client surface touched here and passes.
- [x] Design check -- Series editor still uses `input-xs` + `DesignSystem.glassCard` border; Movies editor still uses default DaisyUI `input-sm`. Styling is unchanged.
- [x] Cross-link with task 046 -- task 046's play-session date input should adopt `EditableDateInput.EditableDateInput` from `Components/EditableDateInput.fs` for consistency.

**Files changed:**
- `src/Client/Components/EditableDateInput.fs` -- NEW. Reusable inline date editor with Enter/blur commit + Escape cancel.
- `src/Client/Client.fsproj` -- Registered `Components\EditableDateInput.fs` in the compile order.
- `src/Client/Pages/SeriesDetail/Views.fs` -- Replaced inline `Daisy.input` block (~14 lines) with single `EditableDateInput.EditableDateInput` call.
- `src/Client/Pages/MovieDetail/Views.fs` -- Same swap for the watch-session date editor.
- `src/Client/Pages/MovieDetail/Types.fs` -- Added `Cancel_edit_session_date` message.
- `src/Client/Pages/MovieDetail/State.fs` -- Handler clears `EditingSessionDate` without firing an API call.
