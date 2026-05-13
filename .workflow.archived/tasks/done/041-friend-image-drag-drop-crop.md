# Task 041: Friend Image Drag-and-Drop Upload with Crop Editor

**Status:** Todo
**Size:** Small
**Created:** 2026-04-09
**Milestone:** --

## Description

Enhance the friend detail page so users can drag and drop an image onto the friend's avatar (from a browser or Windows Explorer) to upload it. After the image is dropped, a crop modal opens immediately, letting the user pan and zoom within a circular crop region. Crop settings persist per friend so the image displays consistently.

## Acceptance Criteria

- [ ] Drag and drop an image file onto the friend avatar area triggers upload
- [ ] Drop sources: browser images (drag from another tab) and local files (Windows Explorer)
- [ ] Crop modal opens automatically after image upload completes
- [ ] Crop modal provides circular crop region with pan (drag) and zoom (scroll/pinch) controls
- [ ] Crop settings (position, zoom) are saved per friend and applied when displaying the avatar
- [ ] An edit/re-crop button allows adjusting the crop after initial upload
- [ ] Existing click-to-upload flow continues to work (also opens crop modal after selection)
- [ ] Visual feedback during drag hover (highlight/border change on the avatar area)

## Implementation Notes

- **Existing patterns:** Drag-and-drop file handling exists in `ContentBlockEditor.fs`; friend image upload API (`uploadFriendImage`) already works
- **Crop settings storage:** Extend `FriendDetail` and `Friend_updated` event to include crop data (x offset, y offset, zoom level)
- **Client-side crop:** Build a Feliz/React component with a glassmorphic modal for the crop editor — circular mask, drag-to-pan, scroll-to-zoom
- **Display:** Use CSS `object-fit: cover` + `object-position` + `transform: scale()` on the avatar, driven by stored crop settings
- **No server-side cropping:** Keep the original image intact; cropping is purely CSS-based using persisted settings

## Dependencies

None

## Work Log

### 2026-04-09

**Changes implemented:**

1. **Shared types** (`src/Shared/Shared.fs`): Added `CropSettings` record type (OffsetX, OffsetY, Zoom). Added `CropSettings` field to `FriendDetail`. Added `saveFriendCropSettings` API method.

2. **Server events** (`src/Server/Friends.fs`): Extended `Friend_updatedData` and `ActiveFriend` with optional crop fields (CropOffsetX, CropOffsetY, CropZoom). Added `Update_crop_settings` command. Updated evolve, decide, and serialization to handle crop data (backward-compatible with existing events via optional fields).

3. **Server projection** (`src/Server/FriendProjection.fs`): Added crop columns to `friend_list` table. Updated projection handler to persist crop data using COALESCE for incremental updates. Added `readCropSettings` helper and updated `getBySlug` to return crop data.

4. **Server API** (`src/Server/Api.fs`): Added `saveFriendCropSettings` endpoint that issues `Update_crop_settings` command.

5. **Client Icons** (`src/Client/Components/Icons.fs`): Added `edit` icon (pencil-square from Heroicons).

6. **Client Types** (`src/Client/Pages/FriendDetail/Types.fs`): Added `CropState` record, `ShowCropModal`, `CropState`, `IsDragOver` to Model. Added messages: `Open_crop_modal`, `Close_crop_modal`, `Update_crop`, `Save_crop`, `Crop_saved`, `Set_drag_over`.

7. **Client State** (`src/Client/Pages/FriendDetail/State.fs`): Initialized crop state from friend data on load. Image upload now resets crop and opens crop modal. Added handlers for all crop-related messages.

8. **Client Views** (`src/Client/Pages/FriendDetail/Views.fs`): 
   - Built `CropEditor` React component with glassmorphic modal, circular crop region, drag-to-pan, scroll-to-zoom, pinch-to-zoom (touch support).
   - Avatar now supports drag-and-drop (dragOver/dragEnter/dragLeave/drop) with visual feedback (ring highlight, scale).
   - Avatar displays images with CSS-based cropping (object-position + transform scale).
   - Added re-crop button (edit icon) on avatar when image exists.
   - Existing click-to-upload flow preserved and also opens crop modal after upload.

**Acceptance Criteria Status:**
- [x] Drag and drop an image file onto the friend avatar area triggers upload
- [x] Drop sources: browser images and local files (Windows Explorer)
- [x] Crop modal opens automatically after image upload completes
- [x] Crop modal provides circular crop region with pan (drag) and zoom (scroll/pinch) controls
- [x] Crop settings (position, zoom) are saved per friend and applied when displaying the avatar
- [x] An edit/re-crop button allows adjusting the crop after initial upload
- [x] Existing click-to-upload flow continues to work (also opens crop modal after selection)
- [x] Visual feedback during drag hover (highlight/border change on the avatar area)

**Build status:** Client build passes. Server build has pre-existing errors in PlaytimeTracker.fs (unrelated to this task). Tests cannot run due to pre-existing server build errors.

**Files changed:**
- src/Shared/Shared.fs
- src/Server/Friends.fs
- src/Server/FriendProjection.fs
- src/Server/Api.fs
- src/Client/Components/Icons.fs
- src/Client/Pages/FriendDetail/Types.fs
- src/Client/Pages/FriendDetail/State.fs
- src/Client/Pages/FriendDetail/Views.fs
