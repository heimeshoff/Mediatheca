# Mediatheca

Personal media library app (movies, series, games, books) built with full-stack F# and event sourcing.

## Build & Run

- `npm run build` - Production client build; also the fastest way to verify Fable compilation (catches type errors and transform issues)
- `npm start` - Run server + client concurrently (dev mode)
- `npm run dev:server` - Server only (dotnet watch, port 5000)
- `npm run dev:client` - Client only (Vite + vite-plugin-fable, port 5173)
- `npm test` - Run Expecto tests (`dotnet run --project tests/Server.Tests/Server.Tests.fsproj`)
- `npm run test:client` - Run client-side unit tests (Vitest driving `*.test.fs` files through `vite.config.mts`'s Fable plugin, Fable.Mocha as the DSL; ADR-0064)

## Tech Stack

- **Backend:** F# / .NET 9 / Giraffe / SQLite (Donald for queries)
- **Frontend:** F# / Fable / Feliz / Elmish (MVU) / React 18
- **Styling:** TailwindCSS 4 + DaisyUI 5
- **RPC:** Fable.Remoting (type-safe, shared types in src/Shared/)
- **Testing:** Expecto with in-memory SQLite
- **Bundler:** Vite 6 with proxy to localhost:5000 for /api/*

## Architecture

- **Event Sourcing + CQRS**: Append-only event store in SQLite, projections for read models
- **DDD Bounded Contexts**: Movies, Journal, Friends, Curation, Intelligence, Integration, Administration
- API routes: `/api/{TypeName}/{MethodName}` via Fable.Remoting
- Shared API contract: `IMediathecaApi` in src/Shared/Shared.fs
- DB file: `mediatheca.db` in the data dir — `DATA_DIR` env var if set, else `~/app/mediatheca/` (on Windows dev: `C:\Users\<user>\app\mediatheca\`). Holds both event store and projections; WAL sidecars and an `images/` cache sit alongside it. See `src/Server/Program.fs`.
- SQLite pragmas: WAL mode, NORMAL sync, FK enabled, 5s busy timeout

## Project Structure

- `src/Shared/` - Shared F# types and API contracts (compiled for both server and client)
- `src/Server/` - ASP.NET Core server (Giraffe, event store, projections)
- `src/Client/` - Fable/Feliz SPA (compiled via vite-plugin-fable, deployed to deploy/public/)
  - `Router.fs` - Page DU and URL parsing
  - `Components/` - Reusable UI (Icons, Sidebar, BottomNav, Layout, PageContainer)
  - `Pages/<Name>/Types.fs|State.fs|Views.fs` - Per-page MVU modules
  - `Types.fs|State.fs|Views.fs` - Root MVU (delegates to child pages via Cmd.map)
  - `App.fs` - Entry point only (CSS import, API proxy, Program.mkProgram)
- `tests/Server.Tests/` - Expecto tests
- `.agentheim/` - Workflow state managed by the `agentheim` plugin: `vision.md`, `context-map.md`, `contexts/<bc>/{backlog,todo,doing,done}/` tasks, `knowledge/{protocol,roadmap}.md`, `knowledge/research/`
- `.workflow.archived/` - Historical record of pre-agentheim tasks (read-only; do not write here)

## Conventions

- Fonts ("Velvet Lobby"): Instrument Serif (`font-display`, headings — mixed case; *italic* is the section-header/wordmark voice), Instrument Sans (`font-sans`, body/UI), Spline Sans Mono (`font-mono`, dates/durations/counts/ids) — all via self-hosted `@fontsource` packages
- Theme: custom "dim" dark theme in `index.css` via `@plugin "daisyui/theme"`, selected by `data-theme="dim"` on `<html>`
- **Paper overlay for all floating surfaces** (ADR-0016, supersedes ADR-0006's mandatory glassmorphism): every dropdown, popover, modal, and floating panel uses **paper overlay** — an opaque fill (`--color-paper`, lighter than the page), a subtle line ring (`--color-line`), and a true elevation shadow (`--shadow-paper`, paper lifted off the page). No translucency, no `backdrop-filter`. Distinct from `.velvet-card` (page/card chrome, flush with the page). See `.paper-overlay` and `.rating-dropdown` in `index.css`, and `DesignSystem.paperOverlay`/`paperDropdown`.
- **Design system canonical artifact:** the authoritative design system is the **live in-app StyleGuide page** (`src/Client/Pages/StyleGuide`), rendering real Feliz specimens backed by `src/Client/DesignSystem.fs` (typed compositions) and `src/Client/index.css` (tokens/values). This running system — not a standalone prose doc — is the source of truth for design-system intent and the frontend task gate (ADR 0015).
- F# modules for code organization (not classes)
- Async workflows for I/O operations
- Event types as discriminated unions per bounded context
- Fable compilation integrated via vite-plugin-fable (no separate dotnet fable step)
- Single-user app — no authentication
- Docker deployment on Linux; development on Windows

## MCP Servers

- **Chrome DevTools** (`chrome-devtools-mcp`): Browser automation for end-to-end smoke testing after UI changes. Configured in `.mcp.json`. Use during `/status` Step 4b to verify pages render correctly, check for console errors, and validate interactive elements. Requires Chrome to be running.

## Gotchas

- F# `open Module.Foo` opens Foo's *contents* — use `open Module` to access `Foo.bar`. Sibling modules in the same namespace are accessible by name without `open`.
- `vite-plugin-fable@0.1.x` requires Vite 6; `0.2.x` requires Vite 7 — don't upgrade one without the other
- `ts-lsp-client@1.1.0` breaks vite-plugin-fable ESM imports — pinned to `1.0.4` via npm overrides
- Warnings from `fable_modules/` vendored code: suppress in `.fsproj` via `<NoWarn>`, never edit vendored files
