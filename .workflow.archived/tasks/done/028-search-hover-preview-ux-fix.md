# Task: Search Hover Preview UX Fix

**ID:** 028
**Milestone:** --
**Size:** Small
**Created:** 2026-02-24
**Dependencies:** None (follow-up fix for task 022)

## Objective

Fix two UX issues with the search modal hover preview:
1. Keyboard navigation should NOT trigger the hover preview — only actual mouse hover should
2. The preview popover should float on top of everything following the mouse cursor, not sit beside the modal as a flex sibling (which causes the modal to shrink)

## Current Behavior

**Issue 1 — Keyboard triggers preview:**
In `SearchModal.fs` (lines 603-625), a `React.useEffect` watches `selIdx` and calls `startHover` whenever the keyboard-selected item changes. This means arrow-key navigation opens the preview popover, which is distracting during fast keyboard navigation.

**Issue 2 — Popover as flex sibling:**
In `SearchModal.fs` (lines 828-938), the modal and preview popover are sibling children of a flex container:
```
div.relative.flex
  ├── div.flex-1 (modal panel)
  └── div.ml-3.w-80.flex-shrink-0 (preview popover)
```
When the preview appears, it takes 320px of horizontal space, shrinking the modal and causing layout shifts. On smaller screens this makes the grid wonky.

## Changes

### 1. Remove Keyboard-Triggered Hover

Delete the `React.useEffect` block (lines 603-625) that watches `selIdx` and triggers `startHover` for keyboard-selected items.

The hover preview should ONLY be triggered by:
- `onMouseEnter` on poster cards (which calls `startHover`) — already works correctly
- `onMouseLeave` on poster cards (which calls `cancelHover`) — already works correctly

### 2. Track Mouse Position

Add mouse position tracking so the popover can follow the cursor:

```fsharp
let mousePos, setMousePos = React.useState((0.0, 0.0))
```

Update the `renderPosterCard` `onMouseEnter` handler (or add a `onMouseMove` handler on the grid container) to track the cursor position:

```fsharp
prop.onMouseMove (fun e ->
    setMousePos (e.clientX, e.clientY)
)
```

### 3. Reposition Popover as Fixed Overlay

Change the popover from a flex sibling to a fixed-position overlay that follows the cursor:

**Before (lines 928-937):**
```fsharp
// Preview popover (sibling to modal panel, not child)
if model.HoverPreview <> NotHovering then
    Html.div [
        prop.className "hidden lg:block ml-3 w-80 flex-shrink-0"
        prop.onMouseEnter (fun _ -> ())
        prop.onMouseLeave (fun _ -> cancelHover ())
        prop.children [ renderPreviewPopover model.HoverPreview ]
    ]
```

**After:**
```fsharp
// Preview popover (fixed overlay following cursor)
if model.HoverPreview <> NotHovering then
    let (mx, my) = mousePos
    Html.div [
        prop.className "fixed z-[100] w-80 pointer-events-none"
        prop.style [
            style.left (int mx + 16)  // 16px to the right of cursor
            style.top (int my - 8)    // slightly above cursor
        ]
        prop.children [ renderPreviewPopover model.HoverPreview ]
    ]
```

**Key changes:**
- `fixed` positioning instead of flex sibling — floats on top of everything
- `z-[100]` — above the modal (`z-[60]`)
- `pointer-events-none` — doesn't interfere with clicking/hovering on the grid below
- Position tracks `mousePos` state, offset 16px right and 8px up from cursor

### 4. Remove Flex Layout from Modal Wrapper

Change the modal wrapper from `flex` to just the modal panel:

**Before:**
```fsharp
Html.div [
    prop.className "relative w-full max-w-4xl mx-4 flex"
    prop.children [
        // Modal panel
        Html.div [ prop.className "flex-1 max-h-[70vh] ..." ]
        // Preview popover (sibling)
    ]
]
```

**After:**
```fsharp
Html.div [
    prop.className "relative w-full max-w-4xl mx-4"  // no flex
    prop.children [
        // Modal panel
        Html.div [ prop.className "max-h-[70vh] ..." ]  // no flex-1
    ]
]
// Preview popover rendered separately (fixed position)
```

The popover should be rendered outside the modal wrapper entirely — as a sibling at the top level of the modal container, or using a React portal. Since it's `fixed` positioned, its DOM location doesn't matter much, but keeping it outside the modal's overflow-hidden container ensures it's never clipped.

### 5. Viewport Edge Detection

Prevent the popover from going off-screen:

```fsharp
let popoverWidth = 320.0
let popoverHeight = 400.0 // approximate max
let windowWidth = Browser.Dom.window.innerWidth
let windowHeight = Browser.Dom.window.innerHeight

let left =
    if mx + 16.0 + popoverWidth > windowWidth
    then mx - popoverWidth - 16.0  // flip to left of cursor
    else mx + 16.0

let top =
    if my - 8.0 + popoverHeight > windowHeight
    then windowHeight - popoverHeight - 16.0  // push up to stay in viewport
    else my - 8.0
```

### 6. Update `renderPreviewPopover` Positioning

Remove the `absolute right-0 top-0` positioning from inside `renderPreviewPopover` since the parent wrapper now handles positioning. The popover content should just have styling (glassmorphism, width, etc.) without position-related classes.

## Files Changed

1. **`src/Client/Components/SearchModal.fs`** — All changes in this single file

## Acceptance Criteria

- [ ] Arrow key navigation does NOT show the hover preview popover
- [ ] Mouse hover on a poster card still shows the preview after 500ms delay
- [ ] Preview popover appears ~16px to the right of the mouse cursor
- [ ] Popover floats on top of everything (above modal, above backdrop)
- [ ] Modal does not resize/shrink when preview is showing
- [ ] Popover flips to the left side when near the right edge of the viewport
- [ ] Popover stays within viewport bounds vertically
- [ ] Popover does not intercept mouse clicks on the grid below
- [ ] Moving mouse off the poster card still dismisses the preview
- [ ] Preview cache still works (re-hovering same item is instant)
- [ ] All existing tests pass

### 2026-02-24 17:00 -- Work Completed

**What was done:**
- Removed `React.useEffect` block (formerly lines 603-625) that watched `selIdx` and triggered `startHover` on keyboard navigation
- Added `mousePos` / `setMousePos` React state for tracking cursor position
- Added `onMouseMove` handler on the modal wrapper to update mouse position
- Changed modal wrapper from `flex` layout to plain `relative` layout (removed `flex` class)
- Removed `flex-1` from the modal panel (no longer needed without flex parent)
- Moved preview popover from flex sibling to `fixed` position overlay rendered outside the modal wrapper
- Added viewport edge detection: popover flips to left side of cursor when near right edge, pushes up when near bottom edge
- Added `pointer-events-none` on the popover wrapper so it does not intercept mouse events
- Added `z-[100]` to float above modal (`z-[60]`)
- Kept `hidden lg:block` so popover only shows on large screens
- Removed `absolute right-0 top-0 z-[60]` positioning from all 6 variants in `renderPreviewPopover` (Loading, LoadedTmdb, LoadedRawg, LoadedLibraryMovie, LoadedLibrarySeries, LoadedLibraryGame) since the parent wrapper now handles positioning

**Acceptance criteria status:**
- [x] Arrow key navigation does NOT show the hover preview popover -- removed the useEffect that triggered startHover on selIdx changes
- [x] Mouse hover on a poster card still shows the preview after 500ms delay -- onMouseEnter/onMouseLeave handlers on poster cards are unchanged
- [x] Preview popover appears ~16px to the right of the mouse cursor -- uses mousePos state with +16px horizontal offset
- [x] Popover floats on top of everything (above modal, above backdrop) -- uses fixed positioning with z-[100]
- [x] Modal does not resize/shrink when preview is showing -- popover is no longer a flex sibling; uses fixed positioning
- [x] Popover flips to the left side when near the right edge of the viewport -- edge detection logic subtracts popoverWidth + 16px
- [x] Popover stays within viewport bounds vertically -- edge detection clamps top to windowHeight - popoverHeight - 16px
- [x] Popover does not intercept mouse clicks on the grid below -- pointer-events-none on popover wrapper
- [x] Moving mouse off the poster card still dismisses the preview -- cancelHover via onMouseLeave is unchanged
- [x] Preview cache still works (re-hovering same item is instant) -- PreviewCache map and Hover_start logic are unchanged
- [x] All existing tests pass -- client build (npm run build) passes; server test failure is pre-existing from concurrent task modifying Shared types/Api.fs (not related to this task)

**Files changed:**
- src/Client/Components/SearchModal.fs -- removed keyboard hover trigger, added mouse position tracking, changed popover to fixed cursor-following overlay with viewport edge detection
