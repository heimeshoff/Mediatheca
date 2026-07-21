---
id: 0018
title: Photino.NET desktop shell — in-process Kestrel, loopback-only, self-contained publish
scope: global
status: accepted
date: 2026-07-20
supersedes: []
superseded_by: []
related_tasks: [infrastructure-w8fnp]
related_research: []
---

# ADR 0018: Photino.NET desktop shell — in-process Kestrel, loopback-only, self-contained publish

> Spike outcome (infrastructure-w8fnp). Mediatheca previously shipped only as a Docker
> container. This records what the desktop-shell prototype proved, and the concrete
> technical decisions it locked in.

## Context

The user wants Mediatheca as a self-standing desktop app on Windows and macOS, without
rewriting the F# codebase. The app is already a web SPA (Fable/Feliz/Elmish) talking to
a local Giraffe/Kestrel server over Fable.Remoting — a shape well suited to a "native
webview + local server" desktop shell, as opposed to a full native rewrite.

Three shapes were considered before prototyping (see task Notes for the original
ranking): a plain launcher that opens the OS browser, a Photino.NET webview shell, and a
Tauri shell with the .NET server as a sidecar process. Photino.NET was chosen to
prototype first: pure .NET, no second language/toolchain, and the server can be hosted
truly in-process (no IPC, no sidecar process to manage).

## Decision

- **Hosting model: in-process Kestrel + Photino.NET webview, one process.** A new
  `src/Desktop/Desktop.fsproj` executable hosts the existing server via a shared
  composition root and opens a native `PhotinoWindow` pointed at it.
- **Composition root extracted, not duplicated.** `src/Server/Program.fs`'s entire
  app-building body (DB init, projections, scheduled jobs, Fable.Remoting API) moved
  into `src/Server/Composition.fs` as `buildApp (args: string[]) (urls: string option) :
  WebApplication`. Both `src/Server/Program.fs` (Docker/CLI entry point, calls with
  `None`) and `src/Desktop/Program.fs` (calls with `Some "http://127.0.0.1:<port>"`)
  call it. There is exactly one place the server is wired up.
- **Loopback-only binding, ephemeral port.** The desktop shell has no way to add
  authentication in scope (ADR-0007), so it must never be network-reachable. It binds a
  `TcpListener` to `127.0.0.1:0` to obtain a free ephemeral port from the OS, releases
  it, then passes `http://127.0.0.1:<port>` to `Composition.buildApp` via
  `WebHost.UseUrls`. Verified with `netstat`: the listening socket shows
  `127.0.0.1:<port>`, never `0.0.0.0:<port>`.
- **Packaging: plain self-contained publish, explicitly no Native AOT.**
  `dotnet publish -r win-x64 --self-contained` / `-r osx-arm64 --self-contained`.
  Fable.Remoting and Giraffe are reflection-heavy (ADR-0004); AOT trimming would break
  the API surface, so `<PublishAot>false</PublishAot>` is explicit in
  `Desktop.fsproj`, not just the default.
- **Data dir gets a macOS default.** `src/Server/DataDir.fs` is a new, pure, unit-tested
  module: `DATA_DIR` env var wins on every platform; otherwise Windows/Linux keep the
  existing `~/app/mediatheca`, and macOS gets
  `~/Library/Application Support/Mediatheca` (the platform convention for per-app
  persistent data). `Composition.buildApp` uses `DataDir.resolveDefault()` instead of
  the inline resolution that used to live in `Program.fs`.

## A real MSBuild obstacle and its fix

Referencing `Server.fsproj` (a `Microsoft.NET.Sdk.Web` **executable** project, needed
for `Program.fs`'s own `[<EntryPoint>]`) from `Desktop.fsproj` (also an executable,
self-contained) trips MSBuild's `NETSDK1150` check: *"A non self-contained executable
cannot be referenced by a self-contained executable."*

The fix that actually works: MSBuild's `ValidateExecutableReferences` task only trusts
`SelfContained` when it arrives as a genuine **command-line global property** on the
outer build (`BuildEngine6.GetGlobalProperties()`), not a value merely set inside a
`.fsproj`'s `<PropertyGroup>`. So:
- `Desktop.fsproj` deliberately does **not** hardcode `<SelfContained>true</SelfContained>`.
  Callers must pass `--self-contained` (or `-p:SelfContained=true`) explicitly on the
  command line — which is exactly what the acceptance-criteria publish command already
  does.
- Its `<ProjectReference Include="..\Server\Server.fsproj">` carries
  `<AdditionalProperties>RuntimeIdentifier=$(RuntimeIdentifier);SelfContained=$(SelfContained)</AdditionalProperties>`
  so the reference build receives the real values.
- `Server.fsproj` has `<SelfContained Condition="'$(RuntimeIdentifier)' != ''">true</SelfContained>`
  and `<RuntimeIdentifiers>win-x64;osx-arm64</RuntimeIdentifiers>` added, but **no
  unconditional `SelfContained`** — so the plain Docker publish command
  (`dotnet publish src/Server/Server.fsproj -c Release -o ...`, no `-r`, no
  `--self-contained`) is untouched. Verified directly: that exact Dockerfile command
  still publishes a framework-dependent `Server.dll` after all these changes.

This is brittle MSBuild trivia, not a design choice — worth a paragraph here so the next
person who touches either `.fsproj` doesn't "simplify" it back into NETSDK1150.

## Spike findings

- **Photino viability: good.** ~100 lines of F# bootstrap (`src/Desktop/Program.fs`):
  find a free loopback port, build+start the app, open a `PhotinoWindow`, block on
  `WaitForClose()`, stop the app. `Photino.NET` 4.0.16 (current on NuGet as of this
  spike) targets net9.0 cleanly.
- **Packaging size:** the win-x64 self-contained publish folder is ~116 MB (.NET
  runtime + WebView2Loader + Photino native + the full `deploy/public` client bundle).
  Unremarkable for a self-contained .NET app; no surprises.
- **Smoke test (Windows, this dev machine):** published, ran `Desktop.exe` directly
  (outside any IDE), confirmed via log output that Kestrel bound to
  `127.0.0.1:<ephemeral port>`, confirmed via `netstat` it was loopback-only, confirmed
  via `curl` that `/health` and `/` (the full SPA `index.html` + built JS/CSS bundle)
  served correctly while the process ran, and confirmed via `tasklist` the process
  stayed alive with no errors in the log. Screenshot-based visual confirmation of the
  rendered webview content was inconclusive: the captured window area is black in this
  environment's remote-desktop screen capture — a known GPU-compositing quirk of
  WebView2 (DirectComposition surfaces) over some remote-desktop screen-capture paths,
  not evidence of an app failure (the HTTP layer under the window was serving correctly
  throughout, and Photino logged successful window creation and `Load()` calls with no
  errors). **Genuinely unverified: what the rendered UI looks like to a human eye on
  this machine.** Recommend a follow-up manual check on a normal (non-remote) desktop
  session before calling this production-ready.
- **macOS (osx-arm64): build/publish verified, runtime unverified.**
  `dotnet publish -r osx-arm64 --self-contained` succeeds cross-compiled from this
  Windows dev machine and produces the expected mac-native bundle (`Photino.Native.dylib`,
  `libe_sqlite3.dylib`, etc.). Nobody has run this on an actual Mac — no Mac available
  in the dev loop. Do not treat this as verified until someone does.
- **What productionizing would still take:** a proper installer (MSI/EXE via WiX or
  similar on Windows, a signed `.app`/`.dmg` on macOS — this spike only produces raw
  publish folders), code signing (unsigned executables trigger SmartScreen/Gatekeeper
  warnings), auto-update (Photino has no built-in updater; would need something like
  Squirrel or a custom check-and-relaunch flow), a tray icon / single-instance guard
  (nothing stops a user from launching the exe twice, each grabbing its own ephemeral
  port and DB connection), and an app icon (none supplied here). Tauri-with-sidecar
  remains the documented fallback if Photino's installer/update story proves
  insufficient later.

## Consequences

### Positive
- One codebase, one composition root, three deployment targets (Docker, Windows
  desktop, macOS desktop) — no forked server setup.
- Desktop targets can never leak the unauthenticated API onto the network (loopback
  bind is structural, not a convention to remember).
- Docker deployment verified unaffected by direct testing of its exact publish command.

### Negative
- The `Server.fsproj` MSBuild changes (conditional `SelfContained`, `RuntimeIdentifiers`,
  the `AdditionalProperties` reference metadata) are non-obvious and must be preserved
  together — see "A real MSBuild obstacle" above.
- No installer, signing, auto-update, or tray/single-instance handling yet — this is a
  prototype, not a shippable desktop app.
- macOS path is publish-verified only, not runtime-verified.

### Neutral
- `Composition.fs` now carries essentially all of `Program.fs`'s former body; it is a
  big function (mirrors the original), not yet decomposed into smaller pieces — fine
  for a spike, a candidate for later cleanup if this becomes a maintained surface.

## Alternatives considered

- **Launcher that opens the default OS browser** — simplest, but no native window chrome,
  taskbar identity, or offline "app" feel; rejected as too thin for the user's ask.
- **Tauri with the .NET server as a sidecar process** — smaller/more polished installer
  and auto-update story out of the box, at the cost of a second toolchain (Rust) and an
  IPC boundary between the shell and the server (vs. Photino's true in-process hosting).
  Kept as the documented fallback, not pursued first.
- **Native AOT publish** — rejected outright per ADR-0004; Fable.Remoting/Giraffe rely
  on reflection.

## References

- `src/Desktop/Desktop.fsproj`, `src/Desktop/Program.fs`
- `src/Server/Composition.fs`, `src/Server/DataDir.fs`, `src/Server/Program.fs`
- `tests/Server.Tests/DataDirTests.fs`
- ADR-0001 (F# full-stack), ADR-0004 (Fable.Remoting/Giraffe reflection), ADR-0007
  (single-user, no auth)
- `.agentheim/contexts/infrastructure/done/infrastructure-w8fnp-photino-desktop-shell-prototype.md`
