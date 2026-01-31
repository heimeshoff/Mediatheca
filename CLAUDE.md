# Mediatheca

Personal media library app (movies, series, games, books) built with full-stack F# and event sourcing.

## Build & Run

- `npm run build` is the fastest way to verify client Fable compilation — catches type errors and transform issues
- `npm start` - Run server + client concurrently (dev mode)
- `npm run dev:server` - Server only (dotnet watch, port 5000)
- `npm run dev:client` - Client only (Vite + vite-plugin-fable, port 5173)
- `npm test` - Run Expecto tests (`dotnet run --project tests/Server.Tests/Server.Tests.fsproj`)
- `npm run build` - Production client build

## Tech Stack

- **Backend:** F# / .NET 9 / Giraffe / SQLite (Donald for queries)
- **Frontend:** F# / Fable / Feliz / Elmish (MVU) / React 18
- **Styling:** TailwindCSS 4 + DaisyUI 5
- **RPC:** Fable.Remoting (type-safe, shared types in src/Shared/)
- **Testing:** Expecto with in-memory SQLite
- **Bundler:** Vite 6 with proxy to localhost:5000 for /api/*

## Architecture

- **Event Sourcing + CQRS**: Append-only event store in SQLite, projections for read models
- **DDD Bounded Contexts**: Catalog, Journal, Friends, Curation, Intelligence, Integration, Administration
- API routes: `/api/{TypeName}/{MethodName}` via Fable.Remoting
- Shared API contract: `IMediathecaApi` in src/Shared/Shared.fs
- DB file: `mediatheca.db` in server's AppContext.BaseDirectory
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
- `.planning/` - PROJECT.md, ROADMAP.md, REQUIREMENTS.md, STATE.md

## Conventions

- Fonts: Oswald (`font-display`, headings) and Inter (`font-sans`, body) via Google Fonts
- Theme: custom "dim" dark theme in `index.css` via `@plugin "daisyui/theme"`, selected by `data-theme="dim"` on `<html>`
- F# modules for code organization (not classes)
- Async workflows for I/O operations
- Event types as discriminated unions per bounded context
- Fable compilation integrated via vite-plugin-fable (no separate dotnet fable step)
- Single-user app — no authentication
- Docker deployment on Linux; development on Windows

## Gotchas

- F# `open Module.Foo` opens Foo's *contents* — use `open Module` to access `Foo.bar`. Sibling modules in the same namespace are accessible by name without `open`.
- `vite-plugin-fable@0.1.x` requires Vite 6; `0.2.x` requires Vite 7 — don't upgrade one without the other
- `ts-lsp-client@1.1.0` breaks vite-plugin-fable ESM imports — pinned to `1.0.4` via npm overrides
- Warnings from `fable_modules/` vendored code: suppress in `.fsproj` via `<NoWarn>`, never edit vendored files
