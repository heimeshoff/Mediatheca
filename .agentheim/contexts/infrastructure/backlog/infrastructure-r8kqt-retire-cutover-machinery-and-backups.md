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

The two-week hold is the rollback window. **Not executable before 2026-08-17.** (Amended
2026-08-05: the wipe-import purge that day created a third server backup with its own
rollback window — executing everything in one pass means waiting until **2026-08-19**; see
Part B and Notes.)

## What

Delete the cutover machinery and its rollback artifacts in one change, restoring
`Composition.fs` to the plain startup path it had before the cutover release.

### Part A — confirm the window has actually elapsed cleanly

Do this first; if anything here looks wrong, stop and do not delete the backups.

- Production has been running the post-cutover image continuously since 2026-08-03 with no
  data-shaped incident.
- Later boots skip the cutover — a restart logs no `[StartupCutover] Phase …` lines (the
  completion marker doing its job). The container has no `sqlite3` binary, so read this off
  the logs rather than the DB. **Grep for `Phase` specifically and expect 0** — every
  healthy boot logs one benign `[StartupCutover] cutover already completed — skipping` line
  until the code is deleted (verified 2026-08-05), so a bare `grep -c StartupCutover`
  reads ≥1 on a perfectly clean window and would look like a failed precondition:

  ```bash
  ssh marco@harbour.elver-minor.ts.net "docker logs --since 24h mediatheca 2>&1 | grep -c 'StartupCutover] Phase'"
  ```

- Drift is still zero: Settings → Projections → drift check, or

  ```bash
  ssh marco@harbour.elver-minor.ts.net "docker exec mediatheca curl -sN http://localhost:5000/api/stream/drift-check | tail -5"
  ```

### Part B — remove the backup files

Server volume (three files, ~55 MB total — inventory verified 2026-08-05):

```bash
ssh marco@harbour.elver-minor.ts.net "docker exec mediatheca ls -la /app/data/backups"
ssh marco@harbour.elver-minor.ts.net "docker exec mediatheca rm \
  /app/data/backups/pre-cutover-20260803-111004.db \
  /app/data/backups/mediatheca-20260803T1110090455038-0f6b4f74.db \
  /app/data/backups/mediatheca-20260805T0844246756407-2e8d8351.db"
```

The third file (added to this list 2026-08-05) is the automatic `VACUUM INTO` backup taken
by `administration-z6ymt`'s wipe-import purge on 2026-08-05 — a *different* rollback point
(post-cutover, pre-purge) with its own two-week window ending **2026-08-19**. Do not delete
it before then; simplest is to run this whole task on/after 2026-08-19 so everything goes
in one pass.

Dev machine (`C:\Users\marco\app\mediatheca\backups\`) — full stale inventory, verified
present 2026-08-05:

```
pre-cutover-2026-08-03\                              (the pristine pre-cutover production copy)
pre-cutover-20260803-011159.db                       (loose dev pre-cutover copies,
pre-cutover-20260803-011704.db                        outside the directory above)
mediatheca-pre-repair-20260802-090956.db             (2026-08-02 live-DB-repair copies)
mediatheca-pre-repair-20260802-091003.db
mediatheca-pre-repair-20260802-091031.db
mediatheca-pre-series-d5tpn-20260802-083050.db       (d5tpn-incident backup + WAL/SHM
mediatheca-pre-series-d5tpn-20260802-083050.db-shm    sidecars)
mediatheca-pre-series-d5tpn-20260802-083050.db-wal
mediatheca-20260731T2240029991076-e949a1f8.db        (generic dated dev backups, both
mediatheca-20260803T0117075747456-039a8c5a.db         pre-cutover and equally stale)
```

All of these predate (or are) the cutover-era rollback points and are equally stale once
the window closes.

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
5. **Delete the permanently-dead Steam-sync gate** (added 2026-08-05, post-purge
   follow-through): `PlaytimeTracker.syncGateOpen`, `migrationCompletedSettingKey`, and the
   `hasLegacyPlayTimeEvents`/gate check inside the sync job (~`PlaytimeTracker.fs:72-86`,
   `:372-382` — re-locate by name), plus `PlaytimeTrackerTests.fs`'s pure `syncGateOpen`
   tests. After `administration-z6ymt`'s live purge (executed 2026-08-05) no
   `Game_play_time_set` row exists and no surviving command can create one, so the gate is
   unconditionally open — it guards against a state that is now unrepresentable. The
   `play_session_migration_completed` settings row in the production DB is a harmless
   orphan; leave it.
6. **Delete the fired one-shot purge tooling** (added 2026-08-05): `src/Server/EventLogFilter.fs`
   and its `Server.fsproj` entry, the `filter-demoted-events` dispatch branch at the top of
   `Program.fs`'s `main`, and `tests/Server.Tests/EventLogFilterTests.fs` with its fsproj
   entry. The purge it existed for ran once and completed (administration-z6ymt, 2026-08-05);
   like `StartupCutover.fs`, git history is the escape hatch. The runbook
   `docs/runbooks/purge-demoted-metadata-events.md` **stays** as the historical record —
   prepend a short "executed 2026-08-05; filter tooling removed by infrastructure-r8kqt"
   note to its header.
7. Keep ADR-0052 in `.agentheim/knowledge/decisions/` as the historical record; append a
   short "retired by infrastructure-r8kqt on <date>" note rather than deleting it.
8. `npm test` and `npm run build` green, then `/deploy`.
9. After the deploy, confirm the first boot of the new image logs no `[StartupCutover]`
   lines at all and the app comes up healthy.

## Acceptance criteria

- [ ] All three server backup files (including the 2026-08-05 wipe-import backup — only
      on/after 2026-08-19) are gone from `/app/data/backups/`, and the dev-machine stale
      inventory listed in Part B is deleted.
- [ ] `grep -rn "StartupCutover" src/ tests/` returns no hits.
- [ ] `Composition.fs` calls `Projection.startAllProjections conn projectionHandlers`
      directly, with no `cutoverBackupOk` binding and no `StartupCutover.run` call.
- [ ] `grep -rn "syncGateOpen\|migrationCompletedSettingKey\|hasLegacyPlayTimeEvents" src/ tests/`
      returns no hits; the Steam sync job dispatches unconditionally.
- [ ] `grep -rn "EventLogFilter\|filter-demoted-events" src/ tests/` returns no hits; the
      purge runbook still exists and carries the executed/retired header note.
- [ ] The silent migration calls listed in Part C step 4 are all still present and unchanged.
- [ ] `npm test` passes and `npm run build` succeeds.
- [ ] ADR-0052 still exists and carries a retirement note naming this task.
- [ ] The deployed container boots healthy with zero `[StartupCutover]` log lines and a
      drift check of 0 discrepancies across all 7 projections.

## Notes

**Refined 2026-09-03 (window-closed grounding pass; promoted to todo):** both rollback
windows have elapsed (2026-08-17 and 2026-08-19). The Part A precondition was run
read-only from the modeling session on 2026-09-03 and is clean, so promotion's gate is met:

- `docker logs --since 24h | grep -c 'StartupCutover] Phase'` → **0**. The benign
  `cutover already completed — skipping` count was also 0, which is expected — the
  container had been up 3 days (healthy), so no boot fell inside the 24h window.
- `/api/stream/drift-check` → `totalDiscrepancies: 0` across all 7 projections
  (Movie, Friend, ContentBlock, Catalog, Series, Game, PlaySession).
- `/app/data/backups` holds exactly the three files Part B lists (~55 MB); the dev-machine
  inventory in Part B was re-verified present, unchanged.

Consequence for the execution split: the server and dev backups are now **expired**
rollback points, not live ones. The one-way-ordering hazard below no longer constrains
sequencing — a `/work` session may run Part C whenever it is claimed, and the builder runs
Part B before or after at their convenience. Part A need not be repeated unless production
has restarted or deployed since 2026-09-03.

Source re-grounded against the working tree at this refinement (all named symbols still
present, nothing moved or renamed; only file-position hints drifted — re-locate by name):
`StartupCutoverTests.fs` is `Server.Tests.fsproj` line 50 (not 42); `EventLogFilter.fs` is
`Server.fsproj` line 58 and `EventLogFilterTests.fs` is `Server.Tests.fsproj` line 51;
the `StartupCutover.run` block in `Composition.fs` sits at ~275–281 (not ~266–274). The
`syncGateOpen` doc-comment cross-reference at `StartupCutover.fs:23` goes away with that
file. Part C step 4's five silent-migration calls are all still present in `Composition.fs`.

`plan.md` disposition settled: it is **tracked**, not untracked — checked in by commit
`648db9c` explicitly as "obsoleted by StartupCutover.fs/ADR-0052, kept for the record". It
stays, on the same reasoning as the purge runbook (Part C step 6); it is out of this task's
scope. The 2026-08-04 note below describing it as untracked is superseded.

**Amended 2026-08-05 (post-purge review, builder-approved):** after `administration-z6ymt`'s
live purge was executed on 2026-08-05, a review of what this task would actually leave
behind produced five amendments, all folded into What/Acceptance criteria above:

1. Part B now includes the third server backup the purge's wipe-import created
   (`mediatheca-20260805T0844246756407-2e8d8351.db`) — its own rollback window ends
   **2026-08-19**, which becomes the effective date for running the whole task in one pass.
   (Promoting on 2026-08-17 and leaving just that one file for two more days is also valid,
   but one pass is simpler.)
2. Part B's dev-machine sweep now lists the full verified stale inventory (loose
   pre-cutover copies, the d5tpn-incident backup + sidecars, two generic dated backups) —
   the original wording named only the directory and the pre-repair files.
3. Part C step 5 (new): the `PlaytimeTracker` Steam-sync gate is deleted — post-purge it
   guards against an unrepresentable state (z6ymt knowingly left it; this task is the
   right place to finish the job).
4. Part C step 6 (new): the fired one-shot purge tooling (`EventLogFilter.fs`, the CLI
   branch, its tests) is deleted; the runbook stays as historical record with an
   executed/retired note. Recorded here so "keep vs delete" is a decision, not an
   accident.
5. Part A's log-check command now greps for `StartupCutover] Phase` (expect 0) — the
   benign `cutover already completed — skipping` line appears once per healthy boot until
   the code is deleted, so the original bare grep would false-alarm.

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
