# UPDATE 2026-08-03 — Phases 4–5 are now FULLY AUTOMATED. The button inventory below is obsolete.

Everything below phase 3 step 7 now happens by itself on the new image's first boot
(`StartupCutover.fs`, ADR-0052), rehearsed successfully end-to-end against a fresh copy of the
production database on 2026-08-03. **Your entire production cutover is now one step:**

    /deploy

(i.e. the usual npm test → docker build → scp → docker load → compose down/up pipeline.)

What first boot does on its own, in order — watch it with `docker logs -f mediatheca` if you like:
1. `[StartupCutover] pre-cutover backup written: .../backups/pre-cutover-<timestamp>.db` —
   a consistent VACUUM INTO copy of the whole DB, taken before anything else touches it.
   (The manual Phase 3 volume copy is now optional belt-and-braces, no longer required.)
2. Silent migrations (cache renames, seed, column drops) — as before.
3. Phase 4 automated: drift check → auto-composed compensating events (in rehearsal: 12 slugs —
   the 11 known from ADR-0051 plus lanterns-2026) → verify zero → SeriesProjection rebuild.
4. Phase 5 automated: dry-run preview (logged in full) → integrity gate → migration (its own
   second backup) → rebuild-all → final drift check. Rehearsal result: 158 streams, 207 events,
   0 integrity failures (goldman-demo's prior-playtime shape is now handled, ADR-0052),
   final drift 0 across all 7 projections.
5. `=== cutover COMPLETE ... Steam-sync gate is open ===` and a completion marker makes every
   later boot skip all of this.

Safety: any unexpected condition logs `!!! CUTOVER ABORTED <reason>` and boots the app normally
on the old data with the Steam gate still closed — nothing destructive runs, and a restart
retries idempotently. A crash mid-cutover self-heals on the next boot (phase-marker guard).

Rehearsal verification against the production copy: Grounded 2282 min ✓, goldman-demo
14 prior + 5 tracked ✓, no fabricated spike days (the two big heatmap days are pre-existing
legacy table rows, preserved verbatim) ✓, post-cutover Steam sync recorded 0 phantom sessions ✓,
TMDB refresh writes the metadata cache (series-t3jkv fixed) ✓, second boot skips ✓.

Rollback if ever needed: stop container, replace mediatheca.db with
`backups/pre-cutover-<timestamp>.db` (delete any -wal/-shm sidecars), start the old image.

The local dev DB (`~/app/mediatheca/`) is the post-cutover rehearsal result; the untouched
pre-cutover copy is in `~/app/mediatheca/backups/pre-cutover-2026-08-03/`.

---

The original plan follows, for reference.

Phase 1 — Right now, before anything is implemented

1. Leave the live app alone. Don't touch Settings > Projections (especially any Rebuild button) and don't run a Cinemarco import on the live container. Those are the only two things that can hurt you today, and normal daily use of the app is completely safe.

Phase 2 — Implementation (dev machine, takes as long as it takes)

2. Run the work skill to execute the todo backlog. The dependency graph enforces the build order automatically: infrastructure-e4kwm and administration-t9bzx first, then administration-c3nvp → series-m7fdk → series-r2xhv → series-q8jwc → series-d5tpn, and in parallel administration-kv7dp, games-w4tzc, games-p6vkz → games-h4mrd + journal-w3sbq. Leave the four backlog items (games-a7dqx, movies-v2gkh, integration-hebjs, administration-z6ymt) where they are.
3. When everything is in done/: confirm npm test and npm run build are green.
4. Rehearse the cutover locally (strongly recommended): copy mediatheca.db from the Docker volume into your local data dir (C:\Users\marco\app\mediatheca\), start the server, and walk Phases 4–5 below against the copy. You get to see the real dry-run numbers and the real drift results with zero risk, and you'll know exactly what "correct" looks like before doing it in production. Delete the local copy's outcome afterwards or keep it as reference.

Phase 3 — Backup and deploy

5. Stop the Docker container.
6. Copy the entire data volume — mediatheca.db, mediatheca.db-wal, mediatheca.db-shm, and the images/ folder — to a dated backup location. This is your rollback point for the whole cutover.
7. Build and deploy the new image, start the container. On first boot the silent migrations run by themselves: cache tables created, series_episodes/series_seasons renamed into the cache tier, series_metadata_cache seeded, demoted columns dropped. No action from you.
8. Sanity check, no buttons: browse the library, open a few series detail pages (metadata now comes from the cache — they should look identical), and check the container logs. You should see the seeding/migration lines, and a line saying the Steam sync was skipped by the gate — that's the gate doing its job, not an error.

Phase 4 — Series cutover (Settings > Projections)

Press things in exactly this order, nothing else on that page:

9. Drift check (read-only, always safe). Expected: zero or near-zero for SeriesProjection. If the two known stragglers appear (love-death-robots-2019, silo-2023-2), apply the composer fix documented in series-d5tpn's ADR.
10. Rebuild SeriesProjection — one deliberate press. This is now safe: the metadata lives in cache tables that survive the rebuild.
11. Drift check again. Expected: 0 discrepancies. Series side done.

Phase 5 — Play-session migration

12. Dry-run preview of the play-session migration (this is pure — cancelling leaves everything untouched). Read the report against expectations: ~157 streams touched, table-covered games = 8, integrity-gate failures = 0, negative deltas skipped = 3 (Grounded, Windrose, Starcom), cursor reconciliations ≤ 3. If anything looks off, cancel and investigate — nothing has happened yet.
13. Run the migration. It takes its own VACUUM INTO backup first, then appends the events and rewinds checkpoints.
14. Rebuild-all — the cutover press the migration flow asks for.
15. Drift check. Expected: 0 for PlaySessionProjection and GameProjection.
16. Eyeball the results: Grounded shows 2282 total minutes, the Journal heatmap has no fabricated 500-hour spike days, and a long-owned game shows the "Xh before tracking + Yh tracked" breakdown.
17. Trigger a Steam sync (or just wait for the scheduled one — the gate is now open). Afterwards, confirm Grounded gained no phantom session — that's the end-to-end proof the cursor carried over correctly.

Phase 6 — Afterwards

18. Keep the Phase 3 backup for a couple of weeks of normal use. The backlog items and the log purge (administration-z6ymt) can be scheduled whenever you like — the purge stays parked until you explicitly promote it.

The complete button inventory, in order: drift check → Rebuild SeriesProjection → drift check → dry-run → migrate → Rebuild-all → drift check → sync. Anything else in the admin area stays unpressed until the drift check reads zero at step 15.
