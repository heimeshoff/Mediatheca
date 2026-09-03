import { test, expect } from "@playwright/test";

// games-t69rb (ADR-0027 harness): the game detail page's two-column grid is
// the page frame — only the content column's children change when the
// Overview/Journal tab switches; the right-hand card column (Links, play
// facets, friends, catalogs, …) must stay mounted at the same DOM node, not
// unmount-and-remount as a visually-identical copy. This spec seeds a game
// via a direct `addGame` API call (hermetic — no TMDB/RAWG/Steam network
// dependency, unlike `addMovie`/`addGameFromSteam`) and asserts DOM node
// identity across the tab switch via `elementHandle.isConnected` — a stale
// handle from an unmounted-and-replaced element goes disconnected, even
// though a new, visually identical element takes its place. Additive-only
// (one new game + one journal save) — no destructive `test.skip` gate
// needed, matching event-tail-follow.smoke.spec.ts's pattern.
test("Game detail: the card column stays the same DOM node across the Overview/Journal tab switch", async ({
    page,
    request,
    baseURL,
}) => {
    test.setTimeout(45_000);

    const gameName = `E2E Persistent Cards ${Date.now()}`;
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
    // (empirically confirmed against the real server — a single-field DU
    // case, not wrapped in an array).
    const addGameBody = (await addGameResponse.json()) as { Ok?: { Created?: string } };
    const slug = addGameBody.Ok?.Created;
    expect(slug).toBeTruthy();

    // A non-blank journal block — so the page would land on Journal-first by
    // default. This spec deliberately clicks back to Overview first so the
    // "switch tabs, card column survives" assertion isn't entangled with the
    // separate default-tab rule (covered by its own client unit tests).
    const saveJournalResponse = await request.post(`${baseURL}/api/IMediathecaApi/saveGameJournal`, {
        data: [
            slug,
            [
                {
                    Id: "e2e-block-1",
                    ParentId: null,
                    BlockType: "text",
                    Content: "Beat the final boss tonight.",
                    Checked: false,
                    Collapsed: false,
                    Language: null,
                    Url: null,
                    ImageRef: null,
                    Caption: null,
                    Position: 0,
                    Width: 1.0,
                },
            ],
        ],
    });
    expect(saveJournalResponse.ok()).toBeTruthy();

    // `lg` breakpoint (Tailwind default 1024px) — the acceptance criterion's
    // two-column-grid viewport.
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`/#/games/${slug}`);

    // The journal has content, so the page opens on Journal-first (the
    // games-t69rb default-tab rule) — switch to Overview first so both tabs
    // get exercised across the assertion below.
    const overviewTab = page.getByRole("button", { name: "Overview" });
    const journalTab = page.getByRole("button", { name: "Journal" });
    await expect(overviewTab).toBeVisible();
    await overviewTab.click();

    // A stable card-column element: the "Links" panel heading is always
    // rendered inside the right-hand card column.
    const linksHeading = page.getByRole("heading", { name: "Links" });
    await expect(linksHeading).toBeVisible();
    const linksHandleBeforeSwitch = await linksHeading.elementHandle();
    expect(linksHandleBeforeSwitch).not.toBeNull();

    await journalTab.click();

    // The Journal tab's block editor renders — confirms the content column
    // actually swapped, not just that nothing happened.
    await expect(page.locator(".journal-block").first()).toBeVisible({ timeout: 10_000 });

    // The card column must still be visible, and — critically — the exact
    // same DOM node: a disconnected handle would mean React tore the whole
    // subtree down and rebuilt a lookalike, which is exactly what games-t69rb
    // set out to stop.
    await expect(linksHeading).toBeVisible();
    const stillConnected = await linksHandleBeforeSwitch!.evaluate((el) => el.isConnected);
    expect(stillConnected).toBe(true);

    const linksHandleAfterSwitch = await linksHeading.elementHandle();
    const isSameNode = await page.evaluate(
        ([a, b]) => a === b,
        [linksHandleBeforeSwitch, linksHandleAfterSwitch]
    );
    expect(isSameNode).toBe(true);
});
