import { test, expect, type Locator } from "@playwright/test";

// design-system-n8zqr: the desktop rail collapses to an icons-only ~64px
// strip via a manual, persisted toggle in the rail header. While collapsed,
// hovering a nav icon reveals its label in a new paper-overlay tooltip
// composition (ADR-0016) -- the system's first tooltip.
//
// Non-destructive (read-only navigation, localStorage writes only) -- no
// `test.skip(!process.env.CI, …)` gate needed, matching the other
// non-destructive specs in this suite.

const STORAGE_KEY = "mediatheca.sidebarCollapsed";

// The rail's width change animates (`transition-[width] duration-200`) —
// bounding-box assertions taken immediately after a toggle click can land
// mid-transition, so callers that care about the settled width poll for it.
async function waitForRailWidth(rail: Locator, expected: number) {
    await expect.poll(async () => (await rail.boundingBox())!.width).toBeCloseTo(expected, -1);
}

test.describe("Sidebar rail: collapsible icons-only toggle", () => {
    test.use({ viewport: { width: 1280, height: 720 } });

    test.beforeEach(async ({ page }) => {
        // Start every test from a clean, first-ever-visit state.
        await page.goto("/#/");
        await page.evaluate(() => localStorage.clear());
        await page.reload();
    });

    test("the toggle has an accessible name and collapses/expands the rail without a page reload", async ({ page }) => {
        const rail = page.locator("aside");

        const expandedBox = (await rail.boundingBox())!;
        expect(expandedBox.width).toBeCloseTo(256, -1);

        const collapseToggle = page.getByRole("button", { name: "Collapse sidebar" });
        await expect(collapseToggle).toBeVisible();

        // A marker on `window` that only survives an in-SPA re-render, not a
        // full page navigation -- proves the collapse is live state, not a
        // reload to some other rendered state.
        await page.evaluate(() => {
            (window as any).__noReloadMarker = true;
        });

        await collapseToggle.click();

        await waitForRailWidth(rail, 64);

        expect(await page.evaluate(() => (window as any).__noReloadMarker)).toBe(true);

        await expect(page.getByRole("button", { name: "Expand sidebar" })).toBeVisible();
    });

    test("while collapsed, no nav label text or tagline is visible", async ({ page }) => {
        const rail = page.locator("aside");

        // Scoped to the rail -- BottomNav (hidden on desktop, still in the
        // DOM) renders its own "Dashboard" dock label, which would otherwise
        // make an unscoped text lookup ambiguous.
        await expect(rail.getByText("Dashboard", { exact: true })).toBeVisible();
        await expect(rail.getByText("Where entertainment lives")).toBeVisible();

        await page.getByRole("button", { name: "Collapse sidebar" }).click();
        await waitForRailWidth(rail, 64);

        await expect(rail.getByText("Dashboard", { exact: true })).toBeHidden();
        await expect(rail.getByText("Where entertainment lives")).toBeHidden();

        // The item is still reachable (aria-label carries the accessible name).
        await expect(page.getByRole("link", { name: "Dashboard" })).toBeVisible();
    });

    test("collapsing shifts main's left edge left by the reclaimed width, with no dead gutter and no page widening", async ({ page }) => {
        const rail = page.locator("aside");
        const main = page.locator("main");

        const railBoxBefore = (await rail.boundingBox())!;
        const mainBoxBefore = (await main.boundingBox())!;
        expect(mainBoxBefore.x).toBeCloseTo(railBoxBefore.x + railBoxBefore.width, 0);

        await page.getByRole("button", { name: "Collapse sidebar" }).click();
        await waitForRailWidth(rail, 64);

        const railBoxAfter = (await rail.boundingBox())!;
        const mainBoxAfter = (await main.boundingBox())!;

        // No dead gutter: main is still flush against the (now narrower) rail.
        expect(mainBoxAfter.x).toBeCloseTo(railBoxAfter.x + railBoxAfter.width, 0);
        // main's left edge moved left by (about) the width the rail gave up.
        expect(mainBoxBefore.x - mainBoxAfter.x).toBeCloseTo(railBoxBefore.width - railBoxAfter.width, -1);

        // min-w-0 still holds: the Dashboard's horizontally-scrolling poster
        // rows don't widen the document past the viewport.
        const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
        expect(scrollWidth).toBeLessThanOrEqual(1280 + 1);
    });

    test("the collapsed state persists across reload, and expanding persists too", async ({ page }) => {
        const rail = page.locator("aside");

        await page.getByRole("button", { name: "Collapse sidebar" }).click();
        await waitForRailWidth(rail, 64);
        expect(await page.evaluate((key) => localStorage.getItem(key), STORAGE_KEY)).toBe("true");

        await page.reload();

        await expect(page.getByRole("button", { name: "Expand sidebar" })).toBeVisible();
        expect((await rail.boundingBox())!.width).toBeCloseTo(64, -1);

        await page.getByRole("button", { name: "Expand sidebar" }).click();
        await waitForRailWidth(rail, 256);
        expect(await page.evaluate((key) => localStorage.getItem(key), STORAGE_KEY)).toBe("false");

        await page.reload();

        await expect(page.getByRole("button", { name: "Collapse sidebar" })).toBeVisible();
        expect((await rail.boundingBox())!.width).toBeCloseTo(256, -1);
    });

    test("with localStorage cleared (first-ever visit), the rail renders expanded", async ({ page }) => {
        // beforeEach already cleared localStorage and reloaded.
        await expect(page.getByRole("button", { name: "Collapse sidebar" })).toBeVisible();
        expect((await page.locator("aside").boundingBox())!.width).toBeCloseTo(256, -1);
    });

    test("while collapsed, hovering a nav icon reveals a paper-overlay tooltip with its label; expanded, hovering reveals none", async ({ page }) => {
        const rail = page.locator("aside");
        const moviesLink = page.getByRole("link", { name: "Movies" });

        // Expanded: the label is already on screen, no tooltip should fire.
        await moviesLink.hover();
        await expect(page.locator(".nav-tooltip")).toHaveCount(0);

        await page.getByRole("button", { name: "Collapse sidebar" }).click();
        await waitForRailWidth(rail, 64);
        await moviesLink.hover();

        const tooltip = page.locator(".nav-tooltip");
        await expect(tooltip).toBeVisible();
        await expect(tooltip).toHaveText("Movies");

        const railBox = (await rail.boundingBox())!;
        const tooltipBox = (await tooltip.boundingBox())!;
        expect(tooltipBox.x).toBeGreaterThanOrEqual(railBox.x + railBox.width);

        // Paper-overlay material (ADR-0016): opaque fill, no backdrop-filter.
        const [background, backdropFilter] = await tooltip.evaluate((el) => {
            const computed = getComputedStyle(el);
            return [computed.backgroundColor, computed.backdropFilter];
        });
        expect(background).not.toBe("rgba(0, 0, 0, 0)");
        expect(backdropFilter === "none" || backdropFilter === "").toBeTruthy();
    });

    test("the active item's treatment is identical collapsed and expanded", async ({ page }) => {
        const rail = page.locator("aside");
        const dashboardLink = page.getByRole("link", { name: "Dashboard" });

        const expandedFill = await dashboardLink.evaluate((el) => getComputedStyle(el).backgroundColor);
        const expandedIcon = await dashboardLink.locator(".nav-item-active-icon").evaluate((el) => getComputedStyle(el).color);

        await page.getByRole("button", { name: "Collapse sidebar" }).click();
        await waitForRailWidth(rail, 64);

        const collapsedFill = await dashboardLink.evaluate((el) => getComputedStyle(el).backgroundColor);
        const collapsedIcon = await dashboardLink.locator(".nav-item-active-icon").evaluate((el) => getComputedStyle(el).color);

        expect(collapsedFill).toBe(expandedFill);
        expect(collapsedIcon).toBe(expandedIcon);
    });
});

// The tall `/#/styleguide` page is used for viewport-pinning assertions —
// see sidebar-rail-viewport-pinned.spec.ts (design-system-vk7rd) for why.
test.describe("Sidebar rail: bottom group stays pinned in both rail states", () => {
    test.use({ viewport: { width: 1280, height: 720 } });

    for (const state of ["expanded", "collapsed"] as const) {
        test(`${state}: the Settings link stays within the viewport at scroll-top and scroll-bottom`, async ({ page }) => {
            await page.goto("/#/styleguide");
            await page.evaluate(() => localStorage.clear());
            await page.reload();

            if (state === "collapsed") {
                await page.getByRole("button", { name: "Collapse sidebar" }).click();
                await waitForRailWidth(page.locator("aside"), 64);
            }

            const settingsLink = page.getByRole("link", { name: "Settings" });
            await expect(settingsLink).toBeVisible();

            const scrollHeight = await page.evaluate(() => document.documentElement.scrollHeight);
            expect(scrollHeight).toBeGreaterThan(720);

            const boxAtTop = (await settingsLink.boundingBox())!;
            expect(boxAtTop.y).toBeGreaterThanOrEqual(0);
            expect(boxAtTop.y + boxAtTop.height).toBeLessThanOrEqual(720);

            await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));

            const boxAtBottom = (await settingsLink.boundingBox())!;
            expect(boxAtBottom.y).toBeCloseTo(boxAtTop.y, 0);
        });
    }
});

test.describe("Sidebar rail: mobile viewport has no rail and no toggle", () => {
    test.use({ viewport: { width: 500, height: 800 } });

    test("the rail is hidden, BottomNav renders instead, and no toggle control exists", async ({ page }) => {
        await page.goto("/#/");

        await expect(page.locator("aside")).toBeHidden();
        await expect(page.getByRole("link", { name: "Settings" })).toBeVisible();

        await expect(page.getByRole("button", { name: "Collapse sidebar" })).toHaveCount(0);
        await expect(page.getByRole("button", { name: "Expand sidebar" })).toHaveCount(0);
    });
});
