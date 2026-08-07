import { test, expect } from "@playwright/test";

// design-system-vk7rd (ADR-0014/ADR-0015 context; prior art t4b9k/grtw7):
// the desktop rail's bottom nav group — originally Admin + Settings, down to
// a single Settings item since administration-k3vmt dissolved the /admin
// console into Settings — was pinned via `mt-auto` against the rail's own
// box, but the rail itself was `min-h-screen` inside a stretching flex row
// — so on any page taller than the viewport the rail grew to document
// height and the "pinned" group scrolled out of sight along with everything
// else. The fix makes the rail `sticky top-0 h-screen`, a real
// viewport-height ceiling, so `mt-auto` resolves against the viewport
// instead of the document.
//
// The `/#/styleguide` route is used as the tall page: it renders a long,
// fixed set of design-system specimens, so it is reliably taller than a
// typical viewport regardless of ambient DB/seed state (unlike a list page,
// whose height depends on how many entities happen to exist).
//
// Non-destructive (read-only navigation, no mutating API calls) — no
// `test.skip(!process.env.CI, …)` gate needed, matching the other
// non-destructive specs in this suite (e.g. event-tail-follow.smoke.spec.ts).

test.describe("Sidebar rail: bottom nav group stays pinned to the viewport", () => {
    test.use({ viewport: { width: 1280, height: 720 } });

    test("the Settings link stays within the viewport at scroll-top and scroll-bottom", async ({ page }) => {
        await page.goto("/#/styleguide");

        const settingsLink = page.getByRole("link", { name: "Settings" });
        await expect(settingsLink).toBeVisible();

        // Confirm the page is actually taller than the viewport, otherwise
        // this spec would pass trivially without exercising the sticky
        // behavior at all.
        const scrollHeight = await page.evaluate(() => document.documentElement.scrollHeight);
        const viewportHeight = 720;
        expect(scrollHeight).toBeGreaterThan(viewportHeight);

        const boxAtTop = (await settingsLink.boundingBox())!;
        expect(boxAtTop.y).toBeGreaterThanOrEqual(0);
        expect(boxAtTop.y + boxAtTop.height).toBeLessThanOrEqual(viewportHeight);

        // Scroll the document (not the rail) to the very bottom.
        await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
        await expect(page.locator("html")).toHaveJSProperty("scrollTop", await page.evaluate(() => document.documentElement.scrollTop));

        const boxAtBottom = (await settingsLink.boundingBox())!;

        // The rail stays put — viewport-relative position is unchanged from
        // the scrolled-to-top measurement, rather than scrolling away with
        // the document.
        expect(boxAtBottom.y).toBeCloseTo(boxAtTop.y, 0);
    });

    test("On a short viewport, the nav scrolls internally so the bottom group remains reachable", async ({ page }) => {
        await page.setViewportSize({ width: 1280, height: 420 });
        await page.goto("/#/styleguide");

        const settingsLink = page.getByRole("link", { name: "Settings" });
        // Scrolled off-screen inside the rail's own nav column initially is
        // fine; what matters is it can be scrolled into view WITHOUT
        // scrolling the document — proving the rail's own `nav` column
        // (overflow-y-auto), not a document scroll, is what brings it into
        // view. Asserting document scrollTop stays 0 is what distinguishes
        // this from the pre-fix behavior, where the whole page (including
        // the then-document-height rail) scrolled together.
        await settingsLink.scrollIntoViewIfNeeded();
        await expect(settingsLink).toBeVisible();

        const documentScrollTop = await page.evaluate(() => document.documentElement.scrollTop || document.body.scrollTop);
        expect(documentScrollTop).toBe(0);

        const box = (await settingsLink.boundingBox())!;
        expect(box.y).toBeGreaterThanOrEqual(0);
        expect(box.y + box.height).toBeLessThanOrEqual(420);
    });

    test("the main column stays flush against the rail with no horizontal gap", async ({ page }) => {
        await page.goto("/#/styleguide");

        const railBox = (await page.locator("aside").boundingBox())!;
        const mainBox = (await page.locator("main").boundingBox())!;

        // `sticky` keeps the rail in normal document flow (unlike `fixed`,
        // which would require a compensating margin/offset on `main`) — the
        // main column should sit immediately to the right of the rail with
        // no gap, exactly as before this change.
        expect(mainBox.x).toBeCloseTo(railBox.x + railBox.width, 0);
    });
});

// design-system-m2wvc (amends ADR-0014 in place): the active sidebar item's
// gold inset-left bar (`box-shadow: var(--ring-active)`) was retracted — the
// burgundy fill (`--color-nav-active-fill`) and the gold icon
// (`--color-gold` via `.nav-item-active-icon`) carry the active state alone.
test.describe("Sidebar rail: active nav item has no inset-left bar", () => {
    test.use({ viewport: { width: 1280, height: 720 } });

    test("the active link has no box-shadow, keeps its burgundy fill, and its icon stays gold", async ({ page }) => {
        await page.goto("/#/");

        const dashboardLink = page.getByRole("link", { name: "Dashboard" });
        await expect(dashboardLink).toBeVisible();

        // Resolve the design-system tokens the same way the browser does,
        // rather than hardcoding an oklch->rgb conversion, so this spec
        // tracks the tokens if their literal values ever move.
        const [expectedFill, expectedGold] = await page.evaluate(() => {
            const probe = document.createElement("div");
            probe.style.backgroundColor = "var(--color-nav-active-fill)";
            probe.style.color = "var(--color-gold)";
            document.body.appendChild(probe);
            const computed = getComputedStyle(probe);
            const result = [computed.backgroundColor, computed.color];
            probe.remove();
            return result;
        });

        const linkStyles = await dashboardLink.evaluate((el) => {
            const computed = getComputedStyle(el);
            return { boxShadow: computed.boxShadow, backgroundColor: computed.backgroundColor };
        });
        expect(linkStyles.boxShadow).toBe("none");
        expect(linkStyles.backgroundColor).toBe(expectedFill);

        const iconColor = await dashboardLink.locator(".nav-item-active-icon").evaluate((el) => getComputedStyle(el).color);
        expect(iconColor).toBe(expectedGold);
    });
});
