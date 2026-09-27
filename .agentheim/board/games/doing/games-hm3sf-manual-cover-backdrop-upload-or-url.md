---
id: games-hm3sf
title: Game detail page — "Change cover" / "Change backdrop" also accept an uploaded image file or a pasted image URL, downloaded to the server's images/ cache, for manual control when RAWG and Steam offer the wrong art
status: doing
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
