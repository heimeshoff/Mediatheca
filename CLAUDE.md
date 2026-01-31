# Mediatheca

Personal media library app (movies, series, games, books) built with full-stack F# and event sourcing.

## Build & Run

- `npm start` - Run server + client concurrently (dev mode)
- `npm run server` - Server only (dotnet watch, port 5000)
- `npm run client` - Client only (Fable watch + Vite, port 5173)
- `npm test` - Run Expecto tests (`dotnet run --project tests/Server.Tests/Server.Tests.fsproj`)
- `npm run build:client` - Production client build

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
- `src/Client/` - Fable/Feliz SPA (output to src/Client/output/, deployed to deploy/public/)
- `tests/Server.Tests/` - Expecto tests
- `.planning/` - PROJECT.md, ROADMAP.md, REQUIREMENTS.md, STATE.md

## Conventions

- F# modules for code organization (not classes)
- Async workflows for I/O operations
- Event types as discriminated unions per bounded context
- Fable output uses `.fs.js` extension
- Single-user app — no authentication
- Docker deployment on Linux; development on Windows
