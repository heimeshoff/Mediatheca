import { test, expect } from "@playwright/test";

// intelligence-cs2dm (ADR-0073 §3/§4): `growingTabArea`'s two render shapes
// (a flat `flex` list of collapsed cards vs. an `invisible`-wrapped copy of
// the same cards next to the grown surface) used to carry no `prop.key`.
// React therefore reconciled them by INDEX: expanding recycled card0's DOM
// node as the `invisible` wrapper and card1's as the surface, unmounting the
// rest, then rebuilt every collapsed card from scratch as children of the
// (repurposed) wrapper — and collapsing did the same in reverse, at one point
// even repurposing the surface's own node as whichever sibling card sat at
// the same index. This spec seeds a game via a direct `addGame` API call
// (hermetic, matching game-detail-persistent-cards.spec.ts's pattern),
// captures the Recently Added card's own `[data-flip-key]` poster tile by
// reference, expands that same card, collapses it again, and asserts the
// tile is still the exact same DOM node (`===`) and still connected — a
// remount would produce a new, visually identical node instead.
test("Games dashboard: a card's collapsed items survive its own expand/collapse round trip", async ({
    page,
    request,
    baseURL,
}) => {
    test.setTimeout(45_000);

    const gameName = `E2E Collapse Persist ${Date.now()}`;
    const addGameResponse = await request.post(`${baseURL}/api/IMediathecaApi/addGame`, {
        data: [
            {
                Name: gameName,
                Year: 2026,
                Genres: [],
                Description: "",
                CoverRef: null,
                BackdropRef: null,
                RawgId: null,
                RawgRating: null,
                SkipDuplicateCheck: true,
            },
        ],
    });
    expect(addGameResponse.ok()).toBeTruthy();
    // AddGameOutcome's `Created` case serializes as `{"Created": "<slug>"}`
    // (see game-detail-persistent-cards.spec.ts).
    const addGameBody = (await addGameResponse.json()) as { Ok?: { Created?: string } };
    const slug = addGameBody.Ok?.Created;
    expect(slug).toBeTruthy();

    await page.setViewportSize({ width: 1280, height: 900 });
    // `/#/games` lands directly on the Games tab (Router.fs / State.fs's
    // `PendingDashboardTab`), so the newly-added game's card is visible
    // without an extra tab click.
    await page.goto("/#/games");

    const recentlyAddedCard = page.locator("#dashboard-collapsed-card-GamesRecentlyAdded");
    await expect(recentlyAddedCard).toBeVisible();
    const seededTile = recentlyAddedCard.locator("[data-flip-key]").first();
    await expect(seededTile).toBeVisible();
    const tileHandleBefore = await seededTile.elementHandle();
    expect(tileHandleBefore).not.toBeNull();

    await recentlyAddedCard.getByRole("button", { name: "Expand" }).click();
    const expandedSurface = page.locator("#dashboard-expanded-card");
    await expect(expandedSurface).toBeVisible();

    await expandedSurface.getByRole("button", { name: "Collapse" }).click();
    // The surface unmounts on collapse; the collapsed card reappears in
    // place (not rebuilt as a lookalike at a different DOM node).
    await expect(page.locator("#dashboard-expanded-card")).toHaveCount(0);
    await expect(recentlyAddedCard).toBeVisible();

    const stillConnected = await tileHandleBefore!.evaluate((el) => el.isConnected);
    expect(stillConnected).toBe(true);

    const tileHandleAfter = await recentlyAddedCard.locator("[data-flip-key]").first().elementHandle();
    const isSameNode = await page.evaluate(([a, b]) => a === b, [tileHandleBefore, tileHandleAfter]);
    expect(isSameNode).toBe(true);
});
