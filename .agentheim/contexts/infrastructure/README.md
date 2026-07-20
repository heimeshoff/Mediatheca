# Infrastructure

**Purpose:** The standing home for globally-true technical concerns — decisions and work that apply to the system as a whole rather than to any single bounded context. Deployment topology, hosting, packaging, runtime configuration, CI/CD, base tooling.

**Classification:** generic

**Key actors:** Single user (developer/operator).

## Scope test

A task belongs here if it passes the routing test: *"if any one BC didn't exist, would this change still need to happen?"* If yes, it's globally true and lands here. BC-local infra concerns (a queue only one BC uses, a BC-specific index) stay in their originating BC.

## Ubiquitous language

- **Deployment target** — a way the app ships to a user: Docker container (Linux, current), Windows desktop app, macOS desktop app. One codebase, multiple targets.
- **Desktop shell** — a native window (webview) hosting the existing web app with the server running in-process; not a rewrite of the client. Candidate technology: Photino.NET.
- **Self-contained publish** — `dotnet publish -r <rid> --self-contained`: runtime bundled, no .NET install required on the target machine. The packaging mode for desktop targets. Native AOT is explicitly *not* used (Fable.Remoting and Giraffe rely on reflection).
- **Data dir** — where `mediatheca.db` and the `images/` cache live. `DATA_DIR` env var if set, else a per-platform default (see `src/Server/Program.fs`). Desktop targets need platform-appropriate defaults (e.g. `~/Library/Application Support/Mediatheca` on macOS).
- **Loopback binding** — desktop targets must bind Kestrel to `127.0.0.1` on an ephemeral/free port, never `0.0.0.0`: the app has no authentication (ADR-0007), so the server must not be reachable from the network when running as a desktop app.

## Invariants

- One codebase serves every deployment target — no per-target forks of domain or client code.
- Desktop targets never expose the unauthenticated server beyond loopback.
- Docker deployment (the current production target) keeps working unchanged as desktop targets are added.
