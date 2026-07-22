import { defineConfig } from "@playwright/test";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

// Fresh, isolated DATA_DIR per test-suite run (ADR-0027) — the real dev DB at
// ~/app/mediatheca/ (or DATA_DIR if the ambient shell already overrides it)
// must never be touched by an e2e run. Computed once here, at config-eval
// time, which Playwright runs before starting any webServer or test —
// exported via process.env so global-teardown.ts (same Node process) can
// find it again to delete it.
const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "mediatheca-e2e-"));
process.env.MEDIATHECA_E2E_DATA_DIR = dataDir;

export default defineConfig({
    testDir: "./tests/e2e",
    timeout: 30_000,
    // One shared dev stack per run (see playwright.config.ts's webServer
    // below) — tests share event-store state, so keep them serial rather
    // than fullyParallel until a real need for isolation-per-test shows up.
    fullyParallel: false,
    workers: 1,
    reporter: "list",
    globalTeardown: "./tests/e2e/global-teardown.ts",
    use: {
        baseURL: "http://localhost:5173",
        trace: "retain-on-failure",
    },
    // No CI pipeline runs this today (ADR-0027) — reuseExistingServer lets a
    // developer keep `npm start` running locally and re-run specs against it
    // without a cold start each time. Set CI=1 (or any truthy value) to force
    // a byte-for-byte reproducible cold start instead, the way a future CI
    // job should.
    webServer: [
        {
            // Non-watch `dotnet run`, not `npm run dev:server` (`dotnet
            // watch`) — see ADR-0027's "dotnet watch teardown" section:
            // killing dotnet watch's top-level process on Windows leaves its
            // child process tree (the watch supervisor) running, orphaned.
            // Plain `dotnet run` dies cleanly when Playwright kills it.
            command: "dotnet run --project src/Server/Server.fsproj",
            port: 5000,
            env: {
                DATA_DIR: dataDir,
                // See ADR-0027 / administration-tj8n2: the two scheduled
                // jobs' catch-up timers race on the shared SqliteConnection
                // and crash the process ~5s after a cold start against an
                // empty store. e2e runs don't exercise scheduled jobs, so
                // skip starting them rather than hit the crash.
                MEDIATHECA_DISABLE_SCHEDULED_JOBS: "1",
            },
            reuseExistingServer: !process.env.CI,
            timeout: 60_000,
        },
        {
            command: "npx vite",
            url: "http://localhost:5173",
            reuseExistingServer: !process.env.CI,
            // Fable's own compile (vite-plugin-fable) is slower to first
            // response than a plain vite dev server — 30s wasn't enough on a
            // cold cache during this spike's own dry run.
            timeout: 60_000,
        },
    ],
});
