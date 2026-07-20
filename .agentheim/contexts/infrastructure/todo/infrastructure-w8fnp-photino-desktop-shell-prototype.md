---
id: infrastructure-w8fnp
title: Photino desktop shell prototype — Kestrel in-process, native webview, self-contained Windows/Mac packaging
status: todo
type: spike
context: infrastructure
created: 2026-07-20
completed:
depends_on: []
blocks: []
tags: [deployment, desktop, photino, packaging, hosting]
related_adrs: [0001, 0004, 0007]
related_research: []
prior_art: []
---

## Why

Mediatheca currently ships only as a Docker container (Linux). The user wants to also run it as a self-standing desktop application on Windows and macOS — without rewriting the F# codebase (a Rust rewrite was considered and rejected: the app is I/O-bound single-user SQLite work, and .NET self-contained publish already covers runtime bundling). The app's existing shape — a web SPA talking to a local Giraffe/Kestrel server — is ideal for a webview desktop shell.

## What

Prototype a Photino.NET desktop shell as a separate project in the repo (e.g. `src/Desktop/`):

- Host the existing Giraffe/Kestrel server **in-process** and open a native Photino window pointed at `http://127.0.0.1:<port>`.
- Reuse the existing server composition from `src/Server/Program.fs` — extract/share the app setup rather than duplicating it; the Docker entry point must keep working unchanged.
- Serve the built client assets (`deploy/public/`) from the shell process.
- Bind to loopback only, on a free ephemeral port — the app has no auth (ADR-0007), so the desktop server must not be network-reachable.
- Package via plain self-contained publish (`dotnet publish -r win-x64 / osx-arm64 --self-contained`). **No Native AOT** — Fable.Remoting and Giraffe rely on reflection (ADR-0004).
- Default the data dir per platform when `DATA_DIR` is unset: keep the current Windows/Linux behavior, add `~/Library/Application Support/Mediatheca` for macOS (small platform switch in the data-dir resolution).

## Acceptance criteria

- [ ] A `src/Desktop/` (or equivalently named) project exists that opens a native Photino window showing the full running app — dashboard renders, Fable.Remoting RPC calls work, images load.
- [ ] The server inside the shell binds to `127.0.0.1` on a free port only; it is not reachable from other machines.
- [ ] `dotnet publish -r win-x64 --self-contained` of the shell produces a standalone folder/exe that runs on Windows without a .NET install; smoke-tested on the dev machine.
- [ ] An `osx-arm64` self-contained publish is scripted/documented (build verified; runtime smoke test deferred — no Mac available in dev, note this in the findings).
- [ ] macOS data-dir default (`~/Library/Application Support/Mediatheca`) is implemented in the shared data-dir resolution and covered by a unit test.
- [ ] Existing Docker/server entry point and `npm` scripts are unaffected (`npm run build` and `npm test` stay green).
- [ ] Spike findings written up in the task's Notes (or an ADR if a real decision crystallizes): Photino viability, packaging sizes, any webview quirks, what productionizing (installers, auto-update, tray icon) would take.

## Notes

- Prior discussion (2026-07-20): options ranked launcher+browser < Photino.NET (recommended) < Tauri sidecar. Photino chosen for prototyping: lightweight cross-platform webview built for .NET, server embeds in one process, ~few hundred lines of bootstrap.
- Photino.NET NuGet: `Photino.NET` (native webview per platform — WebView2 on Windows, WKWebView on macOS). Check current version compatibility with .NET 9.
- Open question for the spike: how the client assets are located at runtime when published (relative to the exe vs. embedded resources).
- Tauri-with-sidecar remains the fallback if Photino disappoints (installer/auto-update polish), at the cost of a thin Rust shell.
- Not a frontend task in the design-system sense — no UI code beyond hosting the existing SPA, so no styleguide dependency.
