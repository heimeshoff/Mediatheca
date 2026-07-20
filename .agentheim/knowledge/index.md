# Index

Top-level catalog of this project's bounded contexts, global decisions, and research.
For BC-scoped artifacts, see each BC's `INDEX.md`.

> Updated by: `model` (BC creation), `work` (global ADRs), `research` (reports tagged global / cross-BC), backfill script.
> Hand-edits are fine but the skills will append at the section markers below.

---

## Bounded contexts

<!-- bc-list:start -->
- **administration** -- **Operational plumbing.** Settings, event store, projection mechanics, event browser, image storage. The infrastructure surface that keeps the single-user app running and inspectable. -- `contexts/administration/INDEX.md`
- **curation** -- User-created **collections** that group media across types — ordered lists of movies / series / games — plus **content blocks** (free-form annotations attached to catalogs and detail pages). The "I made a list" half of the app. -- `contexts/curation/INDEX.md`
- **design-system** -- The **cross-cutting visual language** for Mediatheca's UI. Owns typography, color tokens, the dim theme, the paper-overlay rules for floating surfaces (ADR-0016, retired glassmorphism), Feliz/DaisyUI component patterns, and the in-app StyleGuide page. Gates frontend work in every BC. -- `contexts/design-system/INDEX.md`
- **friends** -- Lightweight registry of **people you experience media with**. Friends are referenced by slug from every other BC; their existence is the foundation of the watched-with / played-with / recommended-by language. -- `contexts/friends/INDEX.md`
- **games** -- Owns the **Game aggregate** — video games with lifecycle status, play modes, family ownership, and Steam / HowLongToBeat metadata. Source of truth for "what am I playing", "how long did it take me", "who shares this title". -- `contexts/games/INDEX.md`
- **infrastructure** -- **Globally-true technical concerns.** Deployment topology, hosting, packaging, runtime configuration, CI/CD, base tooling — decisions that apply to the system as a whole rather than any single BC. Home of the desktop-shell (Photino) deployment work. -- `contexts/infrastructure/INDEX.md`
- **integration** -- **Adapters to external systems.** Translates external shapes (TMDB / RAWG / Steam / HLTB / Jellyfin / Cinemarco) into commands the core BCs accept, and runs scheduled sync jobs that pull external state on a cadence. The anticorruption layer that keeps core BCs free of HTTP and vendor JSON. -- `contexts/integration/INDEX.md`
- **intelligence** -- **Derived insights** over the library and journal. Stats blocks, breakdowns, heatmaps, HLTB comparisons, monthly play-time, watched-with stats. Read-only synthesis layer that answers "how am I doing", not "what should I watch". -- `contexts/intelligence/INDEX.md`
- **journal** -- The **cross-media diary**. Aggregates *when* and *with whom* media was experienced — watch sessions (Movies), episode-watched events (Series), and play-time changes (Games) — into a unified activity timeline. Powers the heatmap, "Recently Watched/Played", and the cross-media stats blocks on the dashboard. -- `contexts/journal/INDEX.md`
- **movies** -- Owns the **Movie aggregate** — a film as a curated library entry with its metadata, posters, ratings, and the watch sessions tied to it. Source of truth for "did I watch this", "with whom", "did I like it". -- `contexts/movies/INDEX.md`
- **series** -- Owns the **Series aggregate** — TV shows with seasons, episodes, rewatch sessions, and episode-level watch state. Source of truth for "what's the next episode", "who watched this with me", "have I finished this run". -- `contexts/series/INDEX.md`
<!-- bc-list:end -->

## Global ADRs (scope: global)

<!-- adr-global:start -->
- **0001** -- F# on both server and client (Fable transpilation) -- 2026-05-12 -- `knowledge/decisions/0001-fsharp-fullstack.md`
- **0002** -- Event sourcing + CQRS as the persistence model -- 2026-05-12 -- `knowledge/decisions/0002-event-sourcing-cqrs.md`
- **0003** -- SQLite as the sole persistence layer -- 2026-05-12 -- `knowledge/decisions/0003-sqlite-persistence.md`
- **0004** -- Fable.Remoting for type-safe client/server RPC -- 2026-05-12 -- `knowledge/decisions/0004-fable-remoting.md`
- **0005** -- Elmish (MVU) as the client architecture -- 2026-05-12 -- `knowledge/decisions/0005-elmish-mvu.md`
- **0006** -- TailwindCSS 4 + DaisyUI 5 with mandatory glassmorphism for overlays *(overlay-material rule superseded by design-system ADR-0016 — paper overlay; Tailwind/DaisyUI/dim-theme decisions still stand)* -- 2026-05-12 -- `knowledge/decisions/0006-tailwind-daisyui-glassmorphism.md`
- **0007** -- Single-user app, no authentication -- 2026-05-12 -- `knowledge/decisions/0007-single-user-no-auth.md`
- **0008** -- Ten bounded contexts for Mediatheca -- 2026-05-12 -- `knowledge/decisions/0008-bounded-contexts-mediatheca.md`
<!-- adr-global:end -->

## Cross-BC research

Research reports relevant to more than one BC (or to the project as a whole). BC-specific
reports are listed in each BC's `INDEX.md`.

<!-- research-global:start -->
<!-- no cross-BC research yet -->
<!-- research-global:end -->

## Pointers

- Vision: `vision.md`
- Context map: `context-map.md` (if exists)
- Protocol (chronological log): `knowledge/protocol.md` -- newest entries on top
- All ADRs: `knowledge/decisions/`
- All research: `knowledge/research/`
