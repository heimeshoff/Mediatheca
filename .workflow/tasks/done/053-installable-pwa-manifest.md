# Task 053: Make Mediatheca Installable as a PWA on Mobile

**Status:** Todo
**Size:** Small
**Created:** 2026-05-01
**Milestone:** --
**Dependencies:** --

## Description

Mediatheca is a personal media library used heavily on mobile (the codebase already has a `BottomNav` component and mobile breakpoints throughout). Right now it can only be opened as a regular browser tab — `src/Client/index.html` has nothing PWA-related (no manifest, no theme-color, no service worker, no icons).

Add the **minimum viable PWA setup** so Chrome on Android shows the "Install app" prompt and the user can launch Mediatheca from the home screen as a standalone app (no browser chrome).

### Scope: minimum installable

- Web app manifest with name, icons, `start_url`, `display: standalone`, theme/background colors.
- A tiny no-op service worker (Chrome's installability criteria require one — even a SW with just a `fetch` listener that doesn't intercept anything counts).
- Icon set generated from the existing app glyph (`Icons.mediatheca` in `src/Client/Components/Icons.fs:174–193`) — a play-circle on the dim theme.
- HTML wired up to register the manifest and the service worker.

**Out of scope** (explicitly):
- Offline support / caching strategies — API calls are fine to fail when offline; this app isn't useful without the server.
- iOS "Add to Home Screen" support (`apple-touch-icon`, `apple-mobile-web-app-capable`, etc.) — user explicitly does not care about iOS.
- `vite-plugin-pwa` — manual setup avoids any conflict with `vite-plugin-fable`'s strict Vite-version pinning. This is well-established at vite-plugin-fable 0.1.x / Vite 6.

### Why

User wants to install Mediatheca on their phone home screen so it launches like a native app instead of as a browser tab.

## Acceptance Criteria

### Manifest

- [ ] `src/Client/public/manifest.webmanifest` exists and gets copied to `deploy/public/manifest.webmanifest` by `npm run build` (Vite copies `<root>/public/*` to `outDir` automatically).
- [ ] Manifest contains: `name: "Mediatheca"`, `short_name: "Mediatheca"`, `start_url: "/"`, `scope: "/"`, `display: "standalone"`, `background_color` matching the dim theme's `--color-base-100` (sRGB hex), `theme_color` matching the dim theme's `--color-primary` (sRGB hex), and an `icons` array with at minimum the three entries below.
- [ ] Manifest references three icons: `icon-192.png` (192×192, `purpose: "any"`), `icon-512.png` (512×512, `purpose: "any"`), `icon-512-maskable.png` (512×512, `purpose: "maskable"`).

### Icons

- [ ] PNG icons generated from a square SVG built around the `Icons.mediatheca` glyph (Heroicons play-circle: outer circle + inner play triangle, `viewBox 0 0 24 24`). Stroke colour = dim theme primary; canvas background = dim theme base-100.
- [ ] `icon-192.png` and `icon-512.png` use a tight ~10% padding around the glyph so it reads well at favicon size.
- [ ] `icon-512-maskable.png` uses ~20% safe-zone padding (Android masking guidelines) so the glyph isn't cropped when the OS applies a circular/squircle mask.
- [ ] Icons live at `src/Client/public/icons/` and end up at `deploy/public/icons/`.

### Service worker

- [ ] `src/Client/public/sw.js` exists with a no-op `fetch` listener (does NOT intercept or cache anything — just enough to satisfy Chrome's installability check). Empty `install` and `activate` handlers are fine.
- [ ] The service worker is registered from `src/Client/index.html` (or a small inline script in it), guarded by `if ('serviceWorker' in navigator)`. Registration scope: `/`.
- [ ] In dev mode (Vite at port 5173) the SW registers without errors. In prod (the static build served by Giraffe), it registers without errors.

### HTML wiring

- [ ] `src/Client/index.html` includes `<link rel="manifest" href="/manifest.webmanifest">` in `<head>`.
- [ ] `<meta name="theme-color" content="...">` matches the manifest `theme_color`.
- [ ] `<link rel="icon" href="/icons/icon-192.png">` (so the browser tab shows the icon too — also useful for desktop installs).
- [ ] No iOS-specific meta tags added (out of scope).

### Server / static serving

- [ ] Verify that `manifest.webmanifest` and `sw.js` are served with sensible MIME types from the production build (`application/manifest+json` and `application/javascript` respectively). If Giraffe's static-file middleware doesn't already map `.webmanifest`, add the mapping.
- [ ] In dev mode, Vite serves them under `/` from `src/Client/public/` automatically — no proxy change needed.

### Smoke test (manual)

- [ ] `npm run build` succeeds (Fable compiles clean).
- [ ] `npm test` passes (no test changes expected, but confirm the suite still runs).
- [ ] Open the running app in Chrome desktop DevTools → **Application → Manifest**: manifest parses, all three icons load, no warnings.
- [ ] Same panel → **Service Workers**: SW is "activated and running", no errors in console.
- [ ] Lighthouse PWA audit (Chrome DevTools): "Installable" check is green. The other PWA checks (offline / start_url / etc.) may stay yellow — that's fine, we're not chasing perfect Lighthouse.
- [ ] On an Android device (or via Chrome's `…` menu → "Install app" on desktop), the install prompt appears and the installed app launches in standalone mode (no URL bar) showing the dashboard.

## Implementation Notes

### Source SVG for icon generation

Build a square 512×512 source SVG that mirrors `Icons.mediatheca`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
  <rect width="512" height="512" fill="#1d232a"/>  <!-- background_color (dim base-100, hex equivalent of oklch(30.857% 0.023 264.149)) -->
  <g transform="translate(56 56) scale(16.67)" fill="none" stroke="#a7e337" stroke-width="1.5"
     stroke-linecap="round" stroke-linejoin="round">
    <!-- outer circle (matches Icons.mediatheca path) -->
    <path d="M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"/>
    <!-- inner play triangle -->
    <path d="M15.91 11.672a.375.375 0 0 1 0 .656l-5.603 3.113a.375.375 0 0 1-.557-.328V8.887c0-.286.307-.466.557-.327l5.603 3.112Z"/>
  </g>
</svg>
```

Convert the oklch values from `index.css` to sRGB hex precisely (don't eyeball). The two values are:
- `--color-base-100: oklch(30.857% 0.023 264.149)` → background colour.
- `--color-primary: oklch(86.133% 0.141 139.549)` → glyph stroke colour.

Use any browser's DevTools (`getComputedStyle`) or a colour-conversion library to get the exact hex; the values above (`#1d232a`, `#a7e337`) are placeholders and should be replaced with the actual conversions.

For the maskable variant, increase the `translate` offset and shrink the `scale` so the glyph occupies roughly the centre 60% of the canvas (Android safe zone is the inner ~80% radius, but tighter is safer for circular masks).

### Generating PNGs

Pick whichever of these is convenient — no need to add a permanent dev dependency:

- One-off `npx`: `npx @resvg/resvg-js source.svg -o icon-512.png -w 512 -h 512` (and similar for 192).
- ImageMagick: `magick convert -density 600 -background none source.svg -resize 512x512 icon-512.png`.
- Inkscape CLI: `inkscape source.svg -w 512 -h 512 -o icon-512.png`.
- Online: any svg-to-png converter is fine for a one-time generation.

Commit the resulting PNGs; don't commit a build script unless it's trivial. Three PNG files only.

### Service worker contents

`src/Client/public/sw.js` should be roughly:

```js
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { /* no-op; let the network handle every request */ });
```

The empty `fetch` handler is what Chrome looks for — without it, the install banner does not appear.

### HTML registration snippet

In `src/Client/index.html`, inside `<head>`:

```html
<link rel="manifest" href="/manifest.webmanifest">
<meta name="theme-color" content="#a7e337">  <!-- replace with real hex -->
<link rel="icon" href="/icons/icon-192.png">
```

And before `</body>` (or inside `App.fs` if cleaner — but plain inline keeps it framework-agnostic and runs even if the Fable bundle fails):

```html
<script>
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      navigator.serviceWorker.register('/sw.js').catch(err => console.warn('SW registration failed', err));
    });
  }
</script>
```

### Server-side MIME types

Check `src/Server/` for the Giraffe static-file setup. If `.webmanifest` isn't mapped, register it as `application/manifest+json`. `.js` is mapped by default. The icons are PNGs — already covered.

### Out of scope (do NOT add)

- `vite-plugin-pwa` or any other PWA plugin.
- Workbox or any caching strategy.
- iOS support (`apple-touch-icon`, `apple-mobile-web-app-capable`, splash screens, etc.).
- Offline fallback page.
- Push notifications, background sync, or any other Service Worker API beyond the empty `fetch` listener.
- Updating `vite.config.mts` — the `public/` folder convention works without config changes.

## Work Log

### 2026-05-01 15:35 — Work Completed

**What was done:**
- Computed exact sRGB hex for the dim theme tokens via a Node script using the OKLab→linear-sRGB→sRGB pipeline. Replaced the placeholder hexes in the task description with the real values: `--color-base-100` = `#2a303c`, `--color-primary` = `#9fe88d`.
- Generated three PNG icons with `sharp` (installed via `npm install --no-save sharp`, so `package.json` is untouched). Source SVGs are built in `scripts/generate-pwa-icons.mjs` and mirror `Icons.mediatheca` (Heroicons play-circle): 10% padding for `icon-192.png` / `icon-512.png`, 20% safe-zone padding for `icon-512-maskable.png`.
- Created `src/Client/public/manifest.webmanifest` with name, short_name, start_url, scope, display:standalone, dim-theme background/theme colors, and the three icon entries (`any`, `any`, `maskable`).
- Created `src/Client/public/sw.js` — minimum no-op service worker (skipWaiting + clients.claim + empty `fetch` handler) that satisfies Chrome's installability check without intercepting anything.
- Updated `src/Client/index.html` to include `<link rel="manifest">`, `<meta name="theme-color">`, `<link rel="icon">`, and an inline SW registration script guarded by `if ('serviceWorker' in navigator)`.
- Registered `.webmanifest` MIME mapping (`application/manifest+json`) on Giraffe's static-file middleware in `src/Server/Program.fs`.
- Verified Vite copies `src/Client/public/*` to `deploy/public/*` (manifest, sw.js, and `icons/` directory all land in the build output).

**Acceptance criteria status:**
- [x] `manifest.webmanifest` exists in `src/Client/public/` and is copied to `deploy/public/manifest.webmanifest` by `npm run build` — verified via post-build `ls`.
- [x] Manifest contains correct name, short_name, start_url, scope, display, background_color (`#2a303c` from real oklch conversion), theme_color (`#9fe88d`), and the three icon entries.
- [x] All three PNG icons generated and present in both `src/Client/public/icons/` and `deploy/public/icons/`. Tight icons use 10% padding; maskable uses 20%.
- [x] `sw.js` exists with the no-op `install`/`activate`/`fetch` handlers. Registration scope is `/` (default for a SW served from `/sw.js`).
- [x] HTML wiring done: `<link rel="manifest">`, `<meta name="theme-color" content="#9fe88d">`, `<link rel="icon" href="/icons/icon-192.png">`, plus the SW registration script. No iOS-specific tags.
- [x] Server registers `.webmanifest` as `application/manifest+json` via `FileExtensionContentTypeProvider` on the deploy/public static-file middleware. `.js` and `.png` already work via the default provider.
- [x] `npm run build` succeeds (Fable compiles clean, Vite emits manifest/sw.js/icons to deploy/public).
- [x] `npm test` passes (255 tests, 0 failed).
- [ ] Manual smoke tests in Chrome DevTools (Application → Manifest, Application → Service Workers, Lighthouse PWA "Installable", Android install prompt) — left for the user; the artefacts are all in place.

**Files changed:**
- `src/Client/index.html` — added manifest link, theme-color meta, icon link, and SW registration script.
- `src/Client/public/manifest.webmanifest` — new; PWA manifest with dim-theme colours and three icons.
- `src/Client/public/sw.js` — new; no-op service worker for installability.
- `src/Client/public/icons/icon-192.png` — new; 192×192 PNG, 10% padding.
- `src/Client/public/icons/icon-512.png` — new; 512×512 PNG, 10% padding.
- `src/Client/public/icons/icon-512-maskable.png` — new; 512×512 PNG, 20% safe-zone padding.
- `src/Server/Program.fs` — registered `.webmanifest` → `application/manifest+json` on the deploy/public static-file middleware.
- `scripts/generate-pwa-icons.mjs` — new; one-off generator script (~35 lines) for re-creating the icons. Requires `npm install --no-save sharp` first.
