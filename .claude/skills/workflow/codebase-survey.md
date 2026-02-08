# Codebase Survey Skill

Use when: Starting a modeling session, beginning a new phase, or when you need a comprehensive snapshot of the current codebase state before making design decisions.

## Purpose

Produce a structured snapshot of the codebase by launching parallel Explore agents that each survey a different architectural layer simultaneously. This gives instant deep context for modeling conversations without sequential file-by-file reading.

## Process

Launch the following 4 agents in parallel using `Task` with `subagent_type: "Explore"`. Each agent reads its target files and produces a concise structured summary.

### Agent 1: Domain Model Survey

**Target files**: `src/Server/Catalog.fs`, `src/Server/Friends.fs`, and any other `src/Server/<Context>.fs` aggregate modules.

**Extract and summarize:**
- Each aggregate's event types (discriminated union cases and their fields)
- Command types
- State types and transitions (NotCreated/Active/Removed)
- The `decide` function — what commands are handled and what validation exists
- The `evolve` function — how each event transforms state
- Cross-aggregate relationships (e.g., friend slugs referenced in movie events)

### Agent 2: API and Shared Types Survey

**Target files**: `src/Shared/Shared.fs`, `src/Server/Api.fs`

**Extract and summarize:**
- The full `IMediathecaApi` interface — every method with its signature
- All DTOs and their fields
- The Slug module and any shared value types
- How API methods map to domain commands/queries
- Any gaps (domain capabilities not yet exposed via API)

### Agent 3: Storage and Projections Survey

**Target files**: `src/Server/EventStore.fs`, `src/Server/MovieProjection.fs`, `src/Server/FriendProjection.fs`, `src/Server/CastStore.fs`, `src/Server/ImageStore.fs`, and any other `*Projection.fs` or `*Store.fs` files.

**Extract and summarize:**
- Event store schema and stream conventions
- Each projection: what events it handles, what read model tables it maintains, table schemas
- Non-event-sourced stores (cast, images) — their table schemas and lifecycle
- How projections are wired up (projection handler registration)

### Agent 4: Client Architecture Survey

**Target files**: `src/Client/Router.fs`, `src/Client/Types.fs`, `src/Client/State.fs`, `src/Client/Views.fs`, `src/Client/App.fs`, `src/Client/Pages/*/Types.fs`, `src/Client/Pages/*/Views.fs`, `src/Client/Components/*.fs`

**Extract and summarize:**
- All pages and their routes (the Page discriminated union)
- Each page's Model, Msg, and key view structure
- Reusable components and their props/interface
- How root MVU delegates to child pages (Cmd.map pattern)
- Current UI patterns (modals, forms, lists, detail views)

## Output

After all 4 agents complete, synthesize their results into a single **Codebase Snapshot** with these sections:

```
## Codebase Snapshot (as of [date])

### Domain Model
[Agent 1 summary]

### API Surface
[Agent 2 summary]

### Storage & Projections
[Agent 3 summary]

### Client Architecture
[Agent 4 summary]

### Cross-Cutting Observations
- [Any gaps, inconsistencies, or patterns noticed across layers]
- [Types defined but not yet used]
- [API methods without UI, or UI without API backing]
```

Write this snapshot to `.planning/ARCHITECTURE.md`, overwriting any previous version. Then present a brief summary to the user confirming the file was written and highlighting any notable changes from the previous snapshot (if one existed).
