import { test, expect, type Page } from "@playwright/test";
import { unlockAdminSections } from "./admin-gate";

// administration-k3vmt: the /admin console dissolved into six inline
// collapsible sections on the Settings page (Events, Projections, Health,
// Images, Jobs, Surgery — `Pages/Settings/{Types,State,Views}.fs`). This
// spec covers the mechanics that are new to that dissolution and aren't
// exercised by the pre-existing Events/Surgery specs:
//   - a collapsed section issues no admin query at all;
//   - the one deliberate exception (`getProjectionStats`, feeding the
//     ADR-0034 dirty banner) fires on every /settings VISIT, never at cold
//     start elsewhere;
//   - a section's load fires once on first expand and never again on
//     re-expand;
//   - collapsing the Events section without navigating stops the live-tail
//     poll via the same trigger navigating away uses (ADR-0023).
// Non-destructive (read-only navigation, no mutating API calls) — no
// `test.skip(!process.env.CI, …)` gate needed, matching the other
// non-destructive specs in this suite.

// administration-k3vmt: each section's outer wrapper (`adminSectionCard` in
// Pages/Settings/Views.fs) carries its own `#settings-admin-<name>` id
// (also the dirty banner's scroll target for Projections) rather than
// `.velvet-card` + a heading filter — the Surgery section nests three more
// `.velvet-card` panels inside it, which would make a class-based locator
// ambiguous.
function adminSectionCheckbox(page: Page, sectionId: string) {
    return page.locator(`#${sectionId}`).locator('input[type="checkbox"]');
}

async function expandSection(page: Page, sectionId: string) {
    // The sections don't exist in the DOM until the danger gate is passed
    // (administration-danger-gate) — unlock first, then toggle.
    await unlockAdminSections(page);
    const checkbox = adminSectionCheckbox(page, sectionId);
    if (!(await checkbox.isChecked())) {
        await checkbox.check();
    }
}

async function collapseSection(page: Page, sectionId: string) {
    const checkbox = adminSectionCheckbox(page, sectionId);
    if (await checkbox.isChecked()) {
        await checkbox.uncheck();
    }
}

/** Every admin-console query goes through `/api/admin/{Method}` (ADR-0017's
 * `IAdminApi`, routed via `AdminRoute.builder`) — tracking this one prefix
 * catches all six sections' load calls without needing a per-endpoint list. */
function trackAdminRequests(page: Page): string[] {
    const requests: string[] = [];
    page.on("request", (req) => {
        const url = req.url();
        if (url.includes("/api/admin/")) {
            requests.push(url);
        }
    });
    return requests;
}

const methodOf = (url: string) => url.split("/api/admin/")[1];

test.describe("Settings' inline administration sections — lazy load + Follow teardown (administration-k3vmt)", () => {
    test("A cold start away from Settings issues no admin query at all — not even getProjectionStats", async ({ page }) => {
        const adminRequests = trackAdminRequests(page);
        await page.goto("/#/");

        // A generous settle window — root init's Cmd.batch fires several
        // async calls; long enough that any stray admin call would have
        // landed by now.
        await page.waitForTimeout(3_000);
        expect(adminRequests).toEqual([]);
    });

    test("Visiting /settings issues exactly one admin query (getProjectionStats) with every section collapsed", async ({ page }) => {
        const adminRequests = trackAdminRequests(page);
        await page.goto("/#/settings");

        // Settle window for the eager Projections load (root
        // State.Url_changed's Settings branch) to land.
        await expect
            .poll(() => adminRequests.length, { timeout: 5_000 })
            .toBeGreaterThan(0);
        await page.waitForTimeout(1_000);

        const methods = adminRequests.map(methodOf);
        expect(methods).toEqual(["getProjectionStats"]);
    });

    test("Expanding a section fires its load once; collapsing and re-expanding fires nothing further", async ({ page }) => {
        const adminRequests = trackAdminRequests(page);
        await page.goto("/#/settings");
        await expect.poll(() => adminRequests.length, { timeout: 5_000 }).toBeGreaterThan(0);
        adminRequests.length = 0; // Discard the eager getProjectionStats load; this test is about Health.

        await expandSection(page, "settings-admin-health");
        await expect.poll(() => adminRequests.filter((u) => methodOf(u) === "getHealthStats").length).toBe(1);

        await collapseSection(page, "settings-admin-health");
        await page.waitForTimeout(500);
        await expandSection(page, "settings-admin-health");

        // Give a re-fetch every chance to happen before asserting it didn't.
        await page.waitForTimeout(1_500);
        expect(adminRequests.filter((u) => methodOf(u) === "getHealthStats").length).toBe(1);
    });

    test("The danger gate hides all six sections until the word is typed, and re-locks on the next visit", async ({ page }) => {
        const sectionIds = [
            "settings-admin-events",
            "settings-admin-projections",
            "settings-admin-health",
            "settings-admin-images",
            "settings-admin-jobs",
            "settings-admin-surgery",
        ];

        await page.goto("/#/settings");
        const gate = page.locator("#settings-admin-unlock");
        await expect(gate).toBeVisible();

        // Not merely hidden — not rendered. Nothing to click through to.
        for (const id of sectionIds) {
            await expect(page.locator(`#${id}`)).toHaveCount(0);
        }

        // A near miss leaves the gate shut.
        await gate.fill("dangerous");
        await expect(page.locator("#settings-admin-surgery")).toHaveCount(0);

        await gate.fill("danger");
        for (const id of sectionIds) {
            await expect(page.locator(`#${id}`)).toBeVisible();
        }
        await expect(gate).toHaveCount(0);

        // Lock without navigating: sections gone, gate back.
        await page.getByRole("button", { name: "Lock" }).click();
        await expect(page.locator("#settings-admin-surgery")).toHaveCount(0);
        await expect(gate).toBeVisible();

        // Leaving and returning re-locks too — the unlock is per-visit model
        // state (`Settings.State.init` runs on every /settings visit), never
        // persisted.
        await gate.fill("danger");
        await expect(page.locator("#settings-admin-surgery")).toBeVisible();
        await page.goto("/#/");
        await page.goto("/#/settings");
        await expect(page.locator("#settings-admin-unlock")).toBeVisible();
        await expect(page.locator("#settings-admin-surgery")).toHaveCount(0);
    });

    test("Collapsing the Events section without navigating stops further getEventsAfter requests (ADR-0023, second trigger)", async ({ page }) => {
        test.setTimeout(45_000);
        const tailRequests: string[] = [];
        page.on("request", (req) => {
            if (req.url().includes("/api/admin/getEventsAfter")) {
                tailRequests.push(req.url());
            }
        });

        await page.goto("/#/settings");
        await expandSection(page, "settings-admin-events");
        await expect(page.locator(".loading-spinner")).toHaveCount(0, { timeout: 8_000 });

        const followButton = page.getByRole("button", { name: /^Follow$/ });
        await expect(followButton).toBeVisible();
        await followButton.click();
        await expect(page.getByRole("button", { name: /^Following$/ })).toBeVisible();

        // Let at least one poll cycle fire (pollIntervalMs ~2s) before acting.
        await page.waitForTimeout(2_500);
        expect(tailRequests.length).toBeGreaterThan(0);

        // Collapse the section — no navigation, same page, same URL. This is
        // the second `stopFollowing` trigger administration-k3vmt adds
        // alongside "leaving Settings" (event-tail-follow.spec.ts's
        // no-orphan-polling (c) covers that one).
        await collapseSection(page, "settings-admin-events");

        await page.waitForTimeout(500);
        const baseline = tailRequests.length;

        await page.waitForTimeout(10_000);
        expect(tailRequests.length).toBe(baseline);
    });
});
