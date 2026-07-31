# Infrastructure

**Purpose:** The standing home for globally-true technical concerns — decisions and work that apply to the system as a whole rather than to any single bounded context. Deployment topology, hosting, packaging, runtime configuration, CI/CD, base tooling.

**Classification:** generic

**Key actors:** Single user (developer/operator).

## Scope test

A task belongs here if it passes the routing test: *"if any one BC didn't exist, would this change still need to happen?"* If yes, it's globally true and lands here. BC-local infra concerns (a queue only one BC uses, a BC-specific index) stay in their originating BC.

## Ubiquitous language

- **Deployment target** — a way the app ships to a user: Docker container (Linux, current, `src/Server/Program.fs`), Windows desktop app, macOS desktop app (both `src/Desktop/`, prototyped in infrastructure-w8fnp / ADR-0018). One codebase, multiple targets.
- **Composition root** — `src/Server/Composition.fs`, specifically `Composition.buildApp (args: string[]) (urls: string option) : WebApplication`. The one place the server (DB init, projections, scheduled jobs, Fable.Remoting API) gets wired up. Every deployment target calls it instead of duplicating setup: `src/Server/Program.fs` (Docker/CLI, `urls = None`, host default binding) and `src/Desktop/Program.fs` (desktop shell, `urls = Some "http://127.0.0.1:<port>"`).
- **Desktop shell** — a native window (webview) hosting the existing web app with the server running in-process; not a rewrite of the client. Implemented with **Photino.NET** (`src/Desktop/`) as of infrastructure-w8fnp — chosen over a plain browser launcher (too thin) and a Tauri sidecar (extra Rust toolchain, IPC boundary); Tauri remains the documented fallback if Photino's installer/auto-update story proves insufficient.
- **Self-contained publish** — `dotnet publish -r <rid> --self-contained`: runtime bundled, no .NET install required on the target machine. The packaging mode for desktop targets (`scripts/publish-desktop-win.ps1`, `scripts/publish-desktop-mac.sh`). Native AOT is explicitly *not* used (Fable.Remoting and Giraffe rely on reflection, ADR-0004) — `Desktop.fsproj` sets `<PublishAot>false</PublishAot>` explicitly. `--self-contained` must be passed on the command line, not hardcoded as `<SelfContained>` in `Desktop.fsproj` — MSBuild's NETSDK1150 executable-reference validation only trusts it as a genuine global property (see ADR-0018 for the mechanics; this is brittle but necessary).
- **Data dir** — where `mediatheca.db` and the `images/` cache live. `DATA_DIR` env var if set, else a per-platform default, now resolved by the pure, unit-tested `src/Server/DataDir.fs` (`DataDir.resolveDefault()`, called from `Composition.buildApp`): Windows/Linux keep `~/app/mediatheca`; macOS gets `~/Library/Application Support/Mediatheca`.
- **Loopback binding** — desktop targets must bind Kestrel to `127.0.0.1` on an ephemeral/free port, never `0.0.0.0`: the app has no authentication (ADR-0007), so the server must not be reachable from the network when running as a desktop app. `src/Desktop/Program.fs` finds a free port itself (bind a `TcpListener` to `127.0.0.1:0`, read back the assigned port, release it) before starting the composition root.
- **Client package pinning rule** — `src/Client/Client.fsproj` pins `Feliz.DaisyUI` to the exact `5.2.0`, not a floating `5.*` (ADR-0036, infrastructure-npyhb). A floating `5.*` silently resolved to `5.3.0`, whose prebuilt `.dll` was built against `Feliz 3.1.1`; the project's own `Feliz 2.*` pin then won the NuGet resolution, producing an incoherent graph (`NU1605` downgrade warning) that manifested as a hard `dotnet build` failure (`FS0193: HtmlHelper` not found) once other unrelated compile errors stopped masking it. `npm run build` (the Fable pathway) was never affected — it compiles DaisyUI from Fable source, not the `.dll`. Taking a future DaisyUI release past `5.2.0` is now an explicit, deliberate act of re-pinning rather than an automatic float. **Prose-only, unenforced** (ADR-0059): `NU1605` is a warning, so an errors-only build gate won't catch a future accidental re-float — the exact pin is itself the structural guard.

## Invariants

- One codebase serves every deployment target — no per-target forks of domain or client code. Enforced structurally: both `Program.fs` entry points call the same `Composition.buildApp`.
- Desktop targets never expose the unauthenticated server beyond loopback.
- Docker deployment (the current production target) keeps working unchanged as desktop targets are added — its exact publish command (`dotnet publish src/Server/Server.fsproj -c Release -o ...`, no `-r`, no `--self-contained`) must keep producing a framework-dependent build; verify this directly (not just "the code still compiles") whenever `Server.fsproj` is touched for desktop-shell reasons.

## Status

- **Windows desktop shell:** prototyped and smoke-tested on a real dev machine (infrastructure-w8fnp) — server starts, binds loopback-only, serves the full SPA + Fable.Remoting API, self-contained publish runs without a .NET install. Not yet productionized: no installer, code signing, auto-update, tray icon, or single-instance guard. See ADR-0018.
- **macOS desktop shell:** publish-verified only (cross-compiled `osx-arm64` self-contained publish succeeds and produces the expected native bundle). Never run on an actual Mac — runtime behavior is unverified.
