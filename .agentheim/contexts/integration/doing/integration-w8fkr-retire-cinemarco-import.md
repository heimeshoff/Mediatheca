---
id: integration-w8fkr
title: Retire the Cinemarco import — delete the Settings card, the `importFromCinemarco` contract member, and `CinemarcoImport.fs`
status: doing
type: refactor
context: integration
created: 2026-08-07
completed:
depends_on: [design-system-001]
blocks: []
tags: [cinemarco, import, removal, settings, adapter]
related_adrs: [0049]
related_research: []
prior_art: []
---

## Why

Cinemarco is the legacy app Mediatheca replaced. Its library was migrated in once, and
that migration is finished — the startup cutover ran COMPLETE on 2026-08-03 with drift
0/7. What remains is an 886-line importer, a Fable.Remoting contract member, and a
Settings card wired to a live "Import" button.

Dead migration code is not neutral here, it is a hazard: the importer explicitly warns
"this will only work on a fresh (empty) Mediatheca database" and rebuilds projections
after running. `plan.md` names it as one of exactly two things in the app that can hurt
the live container ("don't run a Cinemarco import on the live container"). A button that
must never be pressed is better deleted than labelled.

Removing it also shrinks the Integration adapter surface to the systems that are actually
still integrated (TMDB / RAWG / Steam / HLTB / Jellyfin) and drops a stale build-order
constraint from `Server.fsproj`.

## What

Delete the Cinemarco import vertical slice end to end — UI, shared contract, server
adapter — plus its documentation residue. Scope is **the Cinemarco vertical only**: the
three `Game*Backfill.fs` modules stay (ongoing self-draining cache jobs, not migrations),
and `StartupCutover.fs` stays for now (inert, but its own retirement is a separate call).

Already-imported data is untouched: the events Cinemarco produced are ordinary Mediatheca
history and stay in the event store. This removes the *importer*, never its output.

**Client — `src/Client/Pages/Settings/`:**
- `Views.fs` — the `cinemarcoDetail` function (~L1099–1215) and the `integrationCard`
  for "Cinemarco" in the Data Imports section (~L1596–1602). The Data Imports grid then
  holds Steam Family alone; check the `grid-cols-1 sm:grid-cols-2 lg:grid-cols-3` layout
  still reads correctly with one card.
- `Types.fs` — model fields `CinemarcoDbPath`, `CinemarcoImagesPath`, `IsImporting`,
  `ImportResult`; messages `Cinemarco_db_path_changed`, `Cinemarco_images_path_changed`,
  `Start_cinemarco_import`, `Import_completed`. Note these four are the *unprefixed*
  ones — `IsImportingSteam` / `SteamImportResult` / `IsImportingJellyfin` / etc. all
  belong to other adapters and must survive.
- `State.fs` — the init defaults (L149–150) and the four `update` cases (L659–677).

**Shared — `src/Shared/Shared.fs`:**
- `ImportFromCinemarcoRequest` (L1299) and `ImportResult` (L1304). Again: `SteamImportResult`,
  `SteamFamilyImportResult`, and `JellyfinImportResult` are different types and stay.
- `importFromCinemarco` on `IMediathecaApi` (L1556).

**Server:**
- Delete `src/Server/CinemarcoImport.fs` (886 lines).
- `Api.fs` — the `importFromCinemarco` handler (L4385–4388). Its captured dependencies
  (`imageBasePath`, `projectionHandlers`, `httpClient`, `getTmdbConfig`) all have many
  other users in `Api.fs` and stay.
- `Server.fsproj` — the `<Compile Include="CinemarcoImport.fs" />` entry *and* the
  ADR-0049 ordering comment above it, which exists only to explain why `Administration.fs`
  must precede `CinemarcoImport.fs`. That comment is already partly stale (series-d5tpn
  deleted the `lossyRebuildRejectionMessage` branch it describes); with the file gone it
  is wholly moot.

**Documentation:**
- Delete `.documentation/cinemarco-notes.md` (builder decision, 2026-08-07).
- `.agentheim/contexts/integration/README.md` — drop Cinemarco from Purpose, Actors, the
  **Adapter** entry's module list, and rewrite the **Import** entry's example (it currently
  reads "e.g. Cinemarco favorites become Movies + a Catalog"; the Steam library import is
  the natural live replacement).
- `.agentheim/context-map.md` (L52) and `.agentheim/knowledge/index.md` (L20) — drop
  Cinemarco from the Integration purpose lines.
- `.agentheim/contexts/curation/README.md` (L36) — the "Indirect coupling: Cinemarco
  import creates Catalogs" relationship no longer holds. L14's "Cinemarco favorites"
  catalog *name* is a real catalog in the user's library, not a code reference — leave it.
- `.agentheim/contexts/movies/README.md` (L38) — "TMDB / Cinemarco adapters" → TMDB only.

**Explicitly not touched:** ADR-0049 and ADR-0051 (historical decision records — never
rewritten after the fact), `.planning/` (read-only legacy planning archive), and the
`series-d5tpn` / `administration-kv7dp` done-task files.

## Acceptance criteria

- [ ] `rg -i cinemarco src/ tests/ --glob '!**/bin/**' --glob '!**/obj/**'` returns no hits.
- [ ] `.documentation/cinemarco-notes.md` and `src/Server/CinemarcoImport.fs` no longer exist.
- [ ] `src/Server/Server.fsproj` contains no `CinemarcoImport.fs` compile entry and no
      ADR-0049 build-ordering comment.
- [ ] `IMediathecaApi` has no `importFromCinemarco` member, and `Shared.fs` defines neither
      `ImportFromCinemarcoRequest` nor `ImportResult`.
- [ ] `SteamImportResult`, `SteamFamilyImportResult`, `JellyfinImportResult` and every
      `IsImportingSteam` / `IsImportingSteamFamily` / `IsImportingJellyfin` field are still
      present and still wired — the other three import flows are untouched.
- [ ] `npm test` passes (full Expecto suite green, no reduction in test count from deleted
      coverage — there are no Cinemarco tests today).
- [ ] `npm run build` succeeds — Fable compiles the client with no unused-binding or
      incomplete-match warnings introduced by the removal.
- [ ] `/settings` renders: the Data Imports section shows Steam Family and no Cinemarco
      card, and the browser console is clean on load.
- [ ] The integration, curation, and movies BC READMEs, `context-map.md`, and
      `knowledge/index.md` no longer present Cinemarco as a live adapter; the **Import**
      entry in the integration README carries a current example instead.

## Notes

**Prior art / decisions.** No prior integration task covers this. ADR-0049 is linked
because this task deletes an artifact that ADR authored (the `Server.fsproj` ordering
comment) — the decision itself stays on the record untouched. ADR-0051 / `series-d5tpn`
already removed `CinemarcoImport.fs`'s lossy-rebuild fallback branch, so the file's
remaining coupling to `Administration.fs` should be verified gone rather than assumed.

**Order of work.** Server first (delete `CinemarcoImport.fs`, unwire `Api.fs`, edit the
`.fsproj`), then Shared, then Client — deleting the contract member before the client
call site produces a clearer compile error trail than the reverse.

**Not a data migration.** Nothing in this task writes to, reads from, or rebuilds the
event store or any projection. If the work appears to need a projection rebuild, that is
a signal something was mis-scoped — stop and surface it.

**Sequencing against the live container.** Once this ships, `plan.md`'s warning drops from
two hazards to one (Settings → Projections → Rebuild). Worth mentioning to the builder at
completion; updating `plan.md` itself is builder WIP and out of scope here.
