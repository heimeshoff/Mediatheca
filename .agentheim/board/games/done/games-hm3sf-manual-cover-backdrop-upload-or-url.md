---
id: games-hm3sf
title: Game detail page — "Change cover" / "Change backdrop" also accept an uploaded image file or a pasted image URL, downloaded to the server's images/ cache, for manual control when RAWG and Steam offer the wrong art
status: done
type: feature
context: games
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [game-detail, cover, backdrop, image-picker, upload, image-cache]
related_adrs: [0043, 0025, 0016]
related_research: []
prior_art: []
---

## Why

RAWG and Steam sometimes offer the wrong art — a placeholder, the wrong edition, a
low-resolution header, or nothing at all for a game that has neither id. Today the only
way to set a game's cover or backdrop is to pick one of the candidates
`getGameImageCandidates` assembles from those two sources, so a game with bad or
missing source art has no fix. Marco wants **manual control in addition to** the
existing candidate grid: upload a file from disk, or paste a link to an image and have
the server fetch it — either way the image lands in the same server-side `images/`
cache and rides the same event as a picked candidate.

## What

Extend the existing image picker modal on the game detail page
(`src/Client/Pages/GameDetail/Views.fs`, opened by "Change cover" / "Change backdrop")
with two manual entry points, available in both the cover and the backdrop picker,
**alongside** the candidate grid (never replacing it):

1. **Upload a file** — a file input restricted to `image/*`. The client reads the file
   as bytes (the `FileReader` → `Uint8Array` pattern already used by
   `Components/NotesEditor.fs`'s `readFileAsBytes` and `Pages/FriendDetail/Views.fs`)
   and sends them to a new `IMediathecaApi` method, e.g.
   `uploadGameImage: slug -> bytes -> filename -> imageKind -> Async<Result<unit, string>>`
   — same Fable.Remoting byte-array transport `uploadFriendImage` / `uploadContentImage`
   already use; no multipart route.
2. **From URL** — a text field plus a "Use this image" action. The client calls the
   **existing** `selectGameImage slug url imageKind`, which already downloads an arbitrary
   URL server-side and writes it to the fixed ref. No new API method for this path.

Server side, both paths converge on today's `selectGameImage` tail: bytes are written via
`ImageStore.saveImage` to the **fixed ref** `posters/game-{slug}.jpg` (cover) or
`backdrops/game-{slug}.jpg` (backdrop), then `Games.Replace_cover ref` /
`Games.Replace_backdrop ref` is executed through the normal `executeCommand` path so
`Game_cover_replaced` / `Game_backdrop_replaced` fires exactly as it does for a picked
candidate. `CoverRef`/`BackdropRef` stay identity-card projection columns written only by
those events (ADR-0043) — nothing here touches `game_metadata_cache`.

Extract the shared tail (validate → save → command) into one server-side helper so the
URL path and the upload path cannot drift.

**Validation (new, both paths):** the bytes must be a real raster image — sniff the magic
bytes for JPEG, PNG, or WebP (GIF and SVG are refused; SVG in particular is never stored).
Refuse anything else with a clear `Error` and write nothing. For the URL path, additionally
refuse a non-2xx response and a response whose sniffed bytes are not an image (a URL that
resolves to an HTML page must not be stored as a "cover"). Cap the accepted size at 25 MB
on both paths.

**Ref/extension decision — keep the fixed `.jpg` ref.** Steam and RAWG candidates are
already written under the `.jpg` ref regardless of their real encoding (a RAWG screenshot
can be PNG today), and the game-removal path deletes the two refs by their hardcoded
names (`Api.fs`, `posters/game-{slug}.jpg` / `backdrops/game-{slug}.jpg`), so a
format-dependent extension would silently orphan files on removal (ADR-0025's orphan
guard) unless that path were reworked too. No image library is referenced by the server
(no transcoding available). So: a validated PNG/WebP upload is stored as-is under the
`.jpg` ref; the static file server labels it `image/jpeg` and browsers render it by
sniffing, which is exactly the situation the URL path is already in. Record this in the
helper's doc comment; if a worker finds it unacceptable, the alternative is to derive the
extension from the sniffed format **and** change the removal path to delete by the
projection's `CoverRef`/`BackdropRef` instead of the hardcoded names — do not do half of
that.

**Cache busting:** the client already bumps `ImageVersion` on `Image_selected (Ok ())`
and refetches the detail; the two new paths must resolve to the same `Image_selected`
message so the hero, the cover, and the picker's "Current" tile all refresh without a
reload.

**Scope guard:** games only. Movies and Series have no image picker at all today; if the
same manual control is wanted there, capture it separately.

## Acceptance criteria

- [ ] `IMediathecaApi` gains `uploadGameImage` (slug, bytes, filename, image kind) mirroring `uploadFriendImage`'s byte-array shape; `selectGameImage`'s signature is unchanged and it is what the URL path calls.
- [ ] The cover picker and the backdrop picker each show an "Upload image" file input (`accept="image/*"`) and a "From URL" text field with a submit action, in addition to the candidate grid; the grid, its "Current" tile, and its empty-state copy are unchanged.
- [ ] Uploading a JPEG as the cover writes `posters/game-{slug}.jpg`, emits `Game_cover_replaced` with that ref, and the detail page's `CoverRef` afterwards equals that ref (Expecto test through the API/command path with an in-memory store).
- [ ] Uploading a JPEG as the backdrop writes `backdrops/game-{slug}.jpg` and emits `Game_backdrop_replaced` (same test shape).
- [ ] Submitting a URL that returns JPEG bytes stores them under the fixed ref and emits the matching replaced event — the existing behaviour, now covered by a test with a stubbed `HttpClient` handler.
- [ ] A URL returning a non-2xx status, or a 200 whose body is not JPEG/PNG/WebP (e.g. an HTML page), yields `Error` and writes no file; an upload whose bytes are not JPEG/PNG/WebP (a text file renamed `.jpg`, an SVG) yields `Error` and writes no file (tests for each).
- [ ] A payload over 25 MB is refused with `Error` on both paths without being written.
- [ ] Both server paths run through one shared validate-save-command helper (a single function both `selectGameImage` and `uploadGameImage` call); its doc comment records the fixed-`.jpg`-ref decision above.
- [ ] After a successful upload or URL submit the picker closes and the page's cover/backdrop shows the new image without a reload (the `ImageVersion` bump path), and a failure keeps the picker open and shows the error inline. [human-eye]
- [ ] The picker's new controls follow the design system (paper-overlay modal per ADR-0016, existing button/input compositions from `DesignSystem.fs`); `npm run build`, `npm test`, and `npm run test:client` pass.

## Notes

- **Where the pieces already are:** server `Api.fs` `selectGameImage` (downloads any URL, saves to the fixed ref, executes `Replace_cover`/`Replace_backdrop`); `uploadFriendImage` and `uploadContentImage` (byte-array upload via Fable.Remoting, `ImageStore.saveImage`); client `Pages/GameDetail/State.fs` `Open_image_picker` → `Select_image` → `Image_selected` (bumps `ImageVersion`); `Components/NotesEditor.fs` `readFileAsBytes` for the browser-side file read.
- **Related work in other BCs (not same-BC prior art):** administration-xx3mw (image cache admin, ADR-0025 orphan guard — the fixed refs keep the new files inside what it already accounts for); curation-h4k2p (cleanup of uploaded content images on media removal — game covers/backdrops are already deleted on game removal by hardcoded ref).
- **Assumptions made at capture (Marco was not in the loop for follow-ups):** both cover and backdrop get both entry points; the size cap is 25 MB; GIF and SVG are refused; the fixed `.jpg` ref is kept. Any of these is cheap to change while the task is still in todo.
- The existing URL download has no timeout beyond `HttpClient`'s default; leave it, but make sure a failure surfaces as `Error` (it already does via the `with ex` arm).
- No aggregate or event change: `Replace_cover`/`Replace_backdrop` already accept any ref; the games README's Key events list is unchanged. README delta from the worker should add the manual-control sentence to the game detail page layout entry, nothing more.

## Verifier note (iteration 1)

**REASONS:**
- Criterion "A payload over 25 MB is refused with `Error` on both paths without being written" has no test that would fail without the cap. Both cap tests use `oversizedBytes = Array.zeroCreate<byte> (26 * 1024 * 1024)` (`tests/Server.Tests/GameImageUploadTests.fs:54`). Those are all zero bytes, which the magic-byte check `isRasterImage` also rejects (`src/Server/Api.fs:171`). If you delete the `bytes.Length > maxGameImageBytes` branch (`Api.fs:169`), both tests still get an `Error` and still pass. The tests only check for `Error _`, not which refusal happened — so the two cap tests (lines 230, 240) test the format check, not the size cap.
- Checks 2–8 were not run (stop at first failing check). Spot-read looked right: magic bytes correct, GIF/SVG refused, validation before `ImageStore.saveImage`, one shared helper `saveGameImageAndReplace` with the fixed-`.jpg`-ref doc comment, `selectGameImage` signature unchanged, no `.agentheim/` paths, controls use `ModalPanel.viewCustom` with DaisyUI button/input.

**SUGGESTED_FIX:** Make the oversized fixture start with valid JPEG magic bytes (e.g. `FF D8 FF E0` followed by padding past 25 MB) so only the size cap can refuse it. Also assert that the error text names the 25 MB limit, on both the upload and the URL test.

**ITERATION_HINT:** likely-fixable

## Outcome

Extended the game detail page's cover/backdrop picker with manual control, alongside the existing candidate grid (never replacing it):

- **Client** (`src/Client/Pages/GameDetail/`): `Types.fs` gains `Model.ImageUrlText` and `Msg.Image_url_changed` / `Msg.Upload_image_file`. `State.fs`'s `Upload_image_file` handler mirrors `Select_image` exactly — both funnel into the same `Image_selected` result handling (picker closes + `ImageVersion` bump on success, picker stays open + inline error on failure), so the hero/cover/picker "Current" tile refresh identically for both paths and neither can drift from the other. `Views.fs` adds a `readFileAsBytes` helper (the same `FileReader` → `Uint8Array` pattern as `NotesEditor.fs`/`FriendDetail/Views.fs`) and renders the upload input (hidden `<input type="file" accept="image/*">` behind a `btn btn-outline btn-sm` label, the same pattern `AdminProjections/Views.fs` uses) plus a URL text field + "Use this image" button as `ModalPanel.viewCustom`'s `headerExtra`, above the untouched candidate grid. The page-level error alert is now suppressed while the picker is open (`model.ShowImagePicker.IsNone` guard) since a picker-triggered failure shows inline inside the modal instead.
- **Server** (`src/Server/Api.fs`): added `IMediathecaApi.uploadGameImage` (byte-array transport, mirroring `uploadFriendImage`'s shape) and a new private `saveGameImageAndReplace` helper — the single validate-save-command tail both `selectGameImage` (URL path) and `uploadGameImage` (upload path) now call. It sniffs the magic bytes for JPEG/PNG/WebP (refusing GIF/SVG/anything else), caps the payload at 25 MB (`Error "Image exceeds the 25 MB size limit"`), then writes to the game's fixed `posters/game-{slug}.jpg` / `backdrops/game-{slug}.jpg` ref via `ImageStore.saveImage` and executes `Games.Replace_cover`/`Games.Replace_backdrop` through `executeCommandCore`, so `Game_cover_replaced`/`Game_backdrop_replaced` fires exactly as it does for a picked candidate (ADR-0043 — `CoverRef`/`BackdropRef` stay identity-card projection columns written only by those events). `selectGameImage` no longer relies on `EnsureSuccessStatusCode`'s exception path for a non-2xx response — it now checks `response.IsSuccessStatusCode` explicitly and returns a clear `Error`. The helper's doc comment records the fixed-`.jpg`-ref decision from the task's Notes verbatim (why a format-dependent extension isn't used, given the hardcoded-name removal path and ADR-0025's orphan guard).
- **Shared** (`src/Shared/Shared.fs`): `IMediathecaApi.uploadGameImage: string -> byte array -> string -> string -> Async<Result<unit, string>>`; `selectGameImage`'s existing signature is unchanged.
- **Tests** (`tests/Server.Tests/GameImageUploadTests.fs`, registered in `Server.Tests.fsproj`): 9 Expecto tests through the API/command path with an in-memory-backed SQLite store and a stubbed `HttpMessageHandler` — cover upload writes the fixed ref + fires `Game_cover_replaced` + `CoverRef` reflects it; backdrop upload does the same for `Game_backdrop_replaced`/`BackdropRef`; a JPEG URL response stores via the existing `selectGameImage`; a non-2xx URL response, an HTML-body 200 response, a non-image upload, and an SVG upload all yield `Error` and write no file; an oversized upload and an oversized URL response are both refused under the 25 MB cap.
- **Iteration 2 fix (verifier iteration 1 finding):** the two 25 MB-cap tests previously used an all-zero-bytes oversized fixture, which the format-sniff check also rejects — so the tests passed even with the size-cap branch deleted, testing the format check instead of the cap. `oversizedBytes` now starts with valid JPEG SOI+APP0 magic bytes (`FF D8 FF E0 00 10 4A 46 49 46`) padded with zero bytes past 25 MB, so the format check accepts it and only the size cap can refuse it. Both cap tests ("An upload over the 25 MB cap..." and "A URL response over the 25 MB cap...") now assert `Expect.stringContains message "25 MB"` on the `Error` payload instead of matching any `Error _`, confirming the refusal is specifically the size cap (`Api.fs`'s `"Image exceeds the 25 MB size limit"`).

`npm run build`, `npm test` (1089 tests, 0 failed), and `npm run test:client` (132 tests, 0 failed) all pass. The "picker closes on success / stays open with an inline error on failure, without a reload" criterion is UI-only ([human-eye] in the task) — implemented via the shared `Image_selected` handling described above but not covered by an automated test.
