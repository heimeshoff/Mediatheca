---
id: infrastructure-r8kqt
title: Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes
status: backlog
type: chore
context: infrastructure
created: 2026-08-04
completed:
depends_on: []
blocks: []
tags: [cutover, cleanup, deployment, backups, startup, projections]
related_adrs: [0052, 0034]
related_research: []
prior_art: []
---

## Why

`StartupCutover.fs` (ADR-0052) is deliberately temporary code: a one-shot, first-boot
migration harness that ran the series + play-session cutover unattended on production on
2026-08-03 (~11:10 UTC) — 12 compensating events, 158 streams / 207 events migrated, 0
integrity failures, final drift 0 across all 7 projections. The completion marker
`startup_cutover_2026_08_completed` is set in the production database, so the module is
already inert on every later boot.

What remains is dead weight that misleads the next reader: a large module, its test file,
three call sites in `Composition.fs`, and a boot-time indirection (`ensureSafeCatchUp`)
that only ever existed to survive a crash inside the cutover's migrate→rebuild window.
Alongside it, two multi-megabyte rollback copies sit on the server volume and one pristine
pre-cutover copy sits in the dev data dir.

The two-week hold is the rollback window. **Not executable before 2026-08-17.**

## What

Delete the cutover machinery and its rollback artifacts in one change, restoring
`Composition.fs` to the plain startup path it had before the cutover release.

### Part A — confirm the window has actually elapsed cleanly

Do this first; if anything here looks wrong, stop and do not delete the backups.

- Production has been running the post-cutover image continuously since 2026-08-03 with no
  data-shaped incident.
- Later boots skip the cutover — a restart logs no `[StartupCutover] Phase …` lines (the
  completion marker doing its job). The container has no `sqlite3` binary, so read this off
  the logs rather than the DB:

  ```bash
  ssh marco@harbour.elver-minor.ts.net "docker logs --since 24h mediatheca 2>&1 | grep -c StartupCutover"
  ```

- Drift is still zero: Settings → Projections → drift check, or

  ```bash
  ssh marco@harbour.elver-minor.ts.net "docker exec mediatheca curl -sN http://localhost:5000/api/stream/drift-check | tail -5"
  ```

### Part B — remove the backup files

Server volume (both files, ~38 MB total):

```bash
ssh marco@harbour.elver-minor.ts.net "docker exec mediatheca ls -la /app/data/backups"
ssh marco@harbour.elver-minor.ts.net "docker exec mediatheca rm \
  /app/data/backups/pre-cutover-20260803-111004.db \
  /app/data/backups/mediatheca-20260803T1110090455038-0f6b4f74.db"
```

Dev machine — the pristine pre-cutover production copy:

```
C:\Users\marco\app\mediatheca\backups\pre-cutover-2026-08-03\
```

Also sweep `mediatheca-pre-repair-20260802-*.db` if those 2026-08-02 live-DB-repair copies
are still around; they predate the cutover and are equally stale by then.

### Part C — delete the code

Line numbers are as of commit `344d0f6`; re-locate by name rather than trusting them.

1. Delete `src/Server/StartupCutover.fs`; remove `<Compile Include="StartupCutover.fs" />`
   from `src/Server/Server.fsproj` (line 60).
2. Delete `tests/Server.Tests/StartupCutoverTests.fs`; remove
   `<Compile Include="StartupCutoverTests.fs" />` from
   `tests/Server.Tests/Server.Tests.fsproj` (line 42).
3. In `src/Server/Composition.fs`, three edits:
   - Remove the `let cutoverBackupOk = match StartupCutover.backupIfPending …` block and its
     comment (~108–119).
   - Replace `StartupCutover.ensureSafeCatchUp conn projectionHandlers` (~253) with
     `Projection.startAllProjections conn projectionHandlers`, and trim the comment's
     "Routed through StartupCutover so a boot after a crash …" sentence — the surrounding
     ADR-0002 disposable-read-model rationale stays.
   - Remove the `if cutoverBackupOk then StartupCutover.run conn dbPath projectionHandlers`
     block and its comment (~266–274).
4. **Leave the silent migrations alone.** `MetadataCache.initialize`,
   `MetadataCache.seedFromProjections`, `SeriesProjection.dropDeprecatedColumns`,
   `JellyfinStore.migrateFromProjections`, and `GameJournal.migrateFromContentBlocks` are
   permanent, idempotent, marker-gated startup steps — they are not part of this retirement.
5. Keep ADR-0052 in `.agentheim/knowledge/decisions/` as the historical record; append a
   short "retired by infrastructure-r8kqt on <date>" note rather than deleting it.
6. `npm test` and `npm run build` green, then `/deploy`.
7. After the deploy, confirm the first boot of the new image logs no `[StartupCutover]`
   lines at all and the app comes up healthy.

## Acceptance criteria

- [ ] Both server backup files are gone from `/app/data/backups/`, and the dev
      `backups/pre-cutover-2026-08-03/` directory is deleted.
- [ ] `grep -rn "StartupCutover" src/ tests/` returns no hits.
- [ ] `Composition.fs` calls `Projection.startAllProjections conn projectionHandlers`
      directly, with no `cutoverBackupOk` binding and no `StartupCutover.run` call.
- [ ] The silent migration calls listed in Part C step 4 are all still present and unchanged.
- [ ] `npm test` passes and `npm run build` succeeds.
- [ ] ADR-0052 still exists and carries a retirement note naming this task.
- [ ] The deployed container boots healthy with zero `[StartupCutover]` log lines and a
      drift check of 0 discrepancies across all 7 projections.

## Notes

**Refined 2026-08-04 (backlog refinement pass):** goal, scope, and acceptance criteria confirmed
current — no changes needed to the What. Two execution-shape clarifications recorded, following the
administration-z6ymt precedent and ADR-0056 (live actions are operator-executed, never worker-run):

- **Execution split:** Part C (code deletion: `StartupCutover.fs`, its tests, the three
  `Composition.fs` call sites, the ADR-0052 retirement note) plus the `npm test`/`npm run build`
  criteria are **worker-executable** in a normal `/work` run. Parts A and B (production log/drift
  verification and backup deletion over SSH, plus the dev-machine backup sweep) and the final
  deploy-and-confirm criterion are **builder-executed runbook steps** — workers never touch the
  live system. The verifier reports those criteria as "builder runbook pending", not PASS/FAIL.
- **Promotion trigger:** on or after 2026-08-17, the builder confirms Part A's checks look clean,
  then promotes. Promoting earlier would let a work session delete code while the backups are
  still live rollback points — exactly the one-way-ordering hazard the Notes below describe.
- `plan.md` (named below as a deletion candidate) is currently an **untracked** file in the
  builder's working tree — its disposition stays with the builder, not this task.

- **Earliest execution date: 2026-08-17.** Executing sooner throws away the rollback window
  the two-week hold exists to provide.
- **Ordering matters, and it is one-way.** Once the code is deleted, restoring a
  *pre-cutover* backup would leave that database permanently un-migrated — the harness that
  knew how to migrate it is gone. The escape hatch is git history (`344d0f6`) plus ADR-0052.
  So delete the backups and the code together and deliberately, not the code first "to tidy
  up" while still treating the backups as live rollback points.
- Fresh installs are unaffected: `backupIfPending` already no-ops on an empty event log, and
  a new database is built by the current handlers with nothing to cut over.
- Post-cutover verification on 2026-08-04 (a day after the deploy) for the record: scheduled
  Steam sync ran clean, Grounded read 2282 minutes with no phantom session, nightly
  `SeriesRefresh` 39 refreshed / 0 errors.
- Related: ADR-0052 (the automated cutover), ADR-0034 (VACUUM INTO backup discipline),
  `plan.md` (the now-executed cutover plan — a candidate for deletion in this same change if
  it was never committed).
