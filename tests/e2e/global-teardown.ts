import fs from "node:fs";

/// Deletes the temp DATA_DIR created in playwright.config.ts, once for the
/// whole run (ADR-0027). Runs in the same Node process as config eval and
/// after all webServer processes have been torn down, so the directory is no
/// longer in use by the time we remove it.
export default async function globalTeardown() {
    const dir = process.env.MEDIATHECA_E2E_DATA_DIR;
    if (!dir) return;

    // The just-killed `dotnet run` process's SQLite file (and its WAL/SHM
    // sidecars) can stay locked on Windows for a surprisingly long tail
    // after Playwright's webServer teardown resolves — empirically up to
    // ~10-15s during this spike's dry runs (plausibly Defender/Search
    // Indexer scanning the freshly-touched temp files, not the .NET process
    // itself, which is already gone by then). A bare fs.rmSync races that
    // and throws EPERM. Retry with a generous total budget rather than a
    // single long fixed sleep, so the common faster case exits early.
    // Best-effort: a lock that outlives this budget is a transient Windows
    // quirk unrelated to the harness itself (observed during this spike's
    // dry runs, taking longer than any bounded retry budget here on
    // occasion) — the leftover directory lives under the OS temp dir, never
    // shadows the real dev DB, and the OS reclaims temp files on its own
    // schedule regardless. Warn rather than fail the whole run over it.
    const maxAttempts = 10;
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            fs.rmSync(dir, { recursive: true, force: true });
            return;
        } catch (err) {
            if (attempt === maxAttempts) {
                console.warn(
                    `[global-teardown] Could not remove temp DATA_DIR ${dir} after ${maxAttempts} attempts (likely a transient Windows file lock). Leaving it for the OS to reclaim. ${String(err)}`
                );
                return;
            }
            await new Promise((resolve) => setTimeout(resolve, 1000));
        }
    }
}
