---
id: integration-w8fkr
title: Retire the Cinemarco import — delete the Settings card, the `importFromCinemarco` contract member, and `CinemarcoImport.fs`
status: done
type: refactor
context: integration
created: 2026-08-07
completed: 2026-08-07
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

## Outcome

Deleted the Cinemarco import vertical slice end to end. Server: removed
`src/Server/CinemarcoImport.fs` (886 lines), the `importFromCinemarco` handler in
`Api.fs`, and both the `<Compile Include="CinemarcoImport.fs" />` entry and the
ADR-0049 build-ordering comment above it in `Server.fsproj` (the comment was already
partly stale post series-d5tpn; verified the remaining `Administration.fs`/`Api.fs`
ordering has no real dependency left to protect). Shared: removed
`ImportFromCinemarcoRequest`, `ImportResult`, and the `importFromCinemarco` member on
`IMediathecaApi`. Client (`Pages/Settings/`): removed the `cinemarcoDetail` view
function and its `integrationCard` entry (Data Imports grid now holds Steam Family
alone), the four unprefixed model fields (`CinemarcoDbPath`, `CinemarcoImagesPath`,
`IsImporting`, `ImportResult`) and four messages
(`Cinemarco_db_path_changed`/`Cinemarco_images_path_changed`/`Start_cinemarco_import`/
`Import_completed`) from `Types.fs`, and their init defaults + `update` cases from
`State.fs`. Confirmed the Steam/SteamFamily/Jellyfin equivalents (`IsImportingSteam`,
`IsImportingSteamFamily`, `IsImportingJellyfin`, `SteamImportResult`,
`SteamFamilyImportResult`, `JellyfinImportResult`) are untouched. Deleted
`.documentation/cinemarco-notes.md`. Dropped Cinemarco from the integration BC README
(Purpose, Actors, the Adapter entry's module list, and rewrote the Import entry's
example to the Steam library import), `context-map.md` (L52) and
`.agentheim/knowledge/index.md` (L20) Integration purpose lines, the curation README's
now-false "Cinemarco import creates Catalogs" relationship line (L14's "Cinemarco
favorites" catalog *name* deliberately left — it's a real user-library artifact, not a
code reference), and the movies README's adapter line ("TMDB / Cinemarco adapters" →
"TMDB adapter").

`rg -i cinemarco src/ tests/` returns zero hits. `npm run build` compiles clean (no
unused-binding or incomplete-match warnings). `npm test` — full Expecto suite, 676
tests, 0 failed. Live `/settings` browser-console verification was left to the
conductor/verifier per this task's worker instructions (no browser available in this
context) — verified statically instead via the clean Fable build and consistent
Views/Types/State edits.

Not touched, per scope: the three `Game*Backfill.fs` modules, `StartupCutover.fs`,
ADR-0049/ADR-0051 (historical records), `.planning/`, `administration/README.md`'s
historical ADR-0049 passage, and all `INDEX.md` files (conductor-owned).

## Verifier note (iteration 1)

**REASONS:**
- Worker edited a `work`-owned index file: `.agentheim/knowledge/index.md` line 20 (inside the machine-managed `<!-- bc-list:start -->` block). Both check 3 (scope discipline) and check 7 (no protocol/index tampering) name `.agentheim/knowledge/index.md` explicitly as off-limits to workers — indexes are owned by the `work` skill. The worker's `FILE_LIST` confirms the edit, and its `## Outcome` claims "all `INDEX.md` files (conductor-owned)" were left alone, reading the rule narrowly as uppercase-only filenames.
- The task spec itself commissions the violation and cannot be satisfied by any worker as written: `## What` instructs "`.agentheim/knowledge/index.md` (L20) — drop Cinemarco from the Integration purpose lines", and acceptance criterion 9 requires "…`context-map.md`, and `knowledge/index.md` no longer present Cinemarco as a live adapter". A re-dispatched worker that reverts the index hunk to respect the ownership rule then fails criterion 9; a worker that satisfies criterion 9 re-violates check 7. This is a non-converging loop, not a fixable defect — hence the escalation hint below.
- Residual, non-blocking, needs builder eyes rather than a worker pass: acceptance criterion 8 ("`/settings` renders: the Data Imports section shows Steam Family and no Cinemarco card, and the browser console is clean on load") has no runtime evidence. The BC declares no `## Runtime surface`, so check 8 never fires, and the worker recorded no manual-exercise note — its `## Outcome` explicitly defers browser verification ("no browser available in this context"). The DOM-delta half was verified statically; the console-clean half remains unexercised.

**Verified as met (do not redo or over-revert):** `rg -i cinemarco src/ tests/` zero hits; `CinemarcoImport.fs` and `.documentation/cinemarco-notes.md` deleted; `Server.fsproj` clean of the compile entry and ADR-0049 comment with `Administration.fs`/`StartupCutover.fs`/`Api.fs` ordering intact; Shared types/contract member gone; Steam/SteamFamily/Jellyfin import types and fields all survive and stay wired; `npm test` exit 0 (676/676); `npm run build` exit 0, no new warnings; Data Imports grid holds exactly one card (Steam Family); ADR-0049 file unmodified; the cross-BC README/context-map edits are in scope per the task's What — do not revert.

**SUGGESTED_FIX:** Builder/`work` decision needed, not another worker pass. Reword acceptance criterion 9 and the `## What` bullet to drop `.agentheim/knowledge/index.md` from worker scope, revert that one hunk from the worker's commit, and let `work` apply the one-line `bc-list` update itself as index owner. While the task is open, also close criterion 8 with a builder eye-check on `/settings` (Data Imports shows Steam Family only, console clean) or mark that criterion `[human-eye]`, since this BC has no runtime surface for the verifier to drive.

**ITERATION_HINT:** task-under-specified

## Salvage note

Worktree diff salvaged before escalation (ADR-0063): `C:\src\heimeshoff\containers\mediatheca\.agentheim\salvage\integration-w8fkr-escalated-iter1.patch` — the worker's full verified-good diff (committed wip + working tree) against fork point c4f8054, captured before this rollback.

## Resolution (builder decision, 2026-08-07)

The builder reviewed the iteration-1 escalation and directed integration. The verifier's
sole blocking finding was a spec/ownership contradiction, not a code defect: the task
commissions a worker edit to the `work`-owned `.agentheim/knowledge/index.md` bc-list
line. Disposition: the edit is **sanctioned as conductor bookkeeping** — it lands in the
conductor's own squash-merge commit on `main` (the same one-line change the index owner
would have applied), so the ownership rule is honored in the integrating commit even
though the hunk was authored in the worker's branch. All technical acceptance criteria
were independently confirmed met by the verifier (see Verifier note above). Criterion 8's
live browser-console check remains open as a builder eye-check on `/settings`.
