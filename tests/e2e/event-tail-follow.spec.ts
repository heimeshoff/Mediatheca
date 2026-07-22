import { test, expect, type Page, type APIRequestContext } from "@playwright/test";

// administration-a4d9b (ADR-0023, on top of the ADR-0027 harness): codifies
// the Follow toggle's three ADR-0023 behaviors as committed, repeatable
// specs, alongside the harness-proving `event-tail-follow.smoke.spec.ts`.
//
// All events are triggered exclusively via `POST /api/IMediathecaApi/addFriend`
// (additive, hermetic, no seeding/TMDB dependency) — never a destructive
// command — because `playwright.config.ts`'s `reuseExistingServer` can point
// these specs at the real dev DATA_DIR. See ADR-0027's isolation caveat.

/** Fable.Remoting wraps even a single argument in a JSON array; response for
 * a `Result<string, string>`-returning method is `{"Ok": "..."}`. */
async function addFriend(request: APIRequestContext, baseURL: string, name: string): Promise<string> {
    const response = await request.post(`${baseURL}/api/IMediathecaApi/addFriend`, { data: [name] });
    expect(response.ok()).toBeTruthy();
    const body = (await response.json()) as { Ok?: string };
    expect(body.Ok).toBeTruthy();
    return body.Ok!;
}

/** Accumulates every `getEventsAfter` (live-tail poll) request URL seen on
 * this page, observable on the vite-proxied `:5173` baseURL per ADR-0027 —
 * no need to watch `:5000` separately. Must be registered before navigation. */
function trackTailRequests(page: Page): string[] {
    const requests: string[] = [];
    page.on("request", (req) => {
        if (req.url().includes("/api/admin/getEventsAfter")) {
            requests.push(req.url());
        }
    });
    return requests;
}

/** The actual row element (`eventRow` in `EventBrowser/Views.fs`) — scoped to
 * `div.border-b` (every row's own wrapper class, including the conditional
 * `animate-highlight`) rather than a bare `div` locator, so `.first()` can't
 * accidentally resolve to an outer ancestor container that merely happens to
 * contain the same text. */
function friendRowLocator(page: Page, slug: string) {
    return page.locator("div.border-b", { hasText: `Friend-${slug}` }).filter({ hasText: "Friend added" });
}

const followButton = (page: Page) => page.getByRole("button", { name: /^Follow$/ });
const followingButton = (page: Page) => page.getByRole("button", { name: /^Following$/ });

/** Waits for the page's own initial `Load_page` fetch (dispatched at mount,
 * unfiltered) to settle before the test drives any further filter/action —
 * otherwise a same-shape `Load_page` triggered moments later by the test
 * (e.g. typing into the search box) can race the still-in-flight mount
 * fetch, since `Page_loaded` applies whichever response lands last rather
 * than checking which request was newest. Not an ADR-0023 live-tail
 * concern — a plain pagination/load race — so tests route around it here
 * rather than assert on it. */
async function waitForEventsLoaded(page: Page) {
    await expect(page.locator(".loading-spinner")).toHaveCount(0, { timeout: 8_000 });
}

/** Sequential, not `Promise.all` — concurrent commands against the shared
 * `SqliteConnection` intermittently throw ("SqliteConnection does not
 * support nested transactions"), a pre-existing production race already
 * tracked as administration-cx92m (spun off alongside administration-tj8n2's
 * scheduled-job connection race). Out of scope to fix here; these specs
 * simply avoid triggering it by never sending two `addFriend` calls
 * concurrently. */
async function addFriends(request: APIRequestContext, baseURL: string, names: string[]): Promise<string[]> {
    const slugs: string[] = [];
    for (const name of names) {
        slugs.push(await addFriend(request, baseURL, name));
    }
    return slugs;
}

test.describe("Events tab Follow toggle — ADR-0023 live-tail behaviors", () => {
    test("Arrival: a live-appended event becomes visible within a bounded window and carries the arrival-highlight class", async ({
        page,
        request,
        baseURL,
    }) => {
        trackTailRequests(page);
        await page.goto("/#/admin/events");
        await waitForEventsLoaded(page);

        await expect(followButton(page)).toBeVisible();
        await followButton(page).click();
        await expect(followingButton(page)).toBeVisible();

        const friendName = `E2E Arrival Friend ${Date.now()}`;
        const slug = await addFriend(request, baseURL!, friendName);

        // Poll cadence is ~2s (EventBrowser/State.fs's pollIntervalMs) — a
        // bounded window of a couple of cycles, not a generous sleep that
        // would mask the real cadence.
        const row = friendRowLocator(page, slug);
        await expect(row.first()).toBeVisible({ timeout: 4_000 });

        // NewlyArrived is replaced wholesale only on the *next* Tail_loaded
        // batch, and this test creates no further events, so the highlight
        // class is reliably still present — the actual mechanism
        // (DesignSystem.animateHighlight / Set.contains against
        // Model.NewlyArrived), not an invented perceptual proxy.
        await expect(row.first()).toHaveClass(/animate-highlight/);
    });

    test("Filter-respecting live rows: a matching live event arrives, a non-matching one stays absent", async ({
        page,
        request,
        baseURL,
    }) => {
        trackTailRequests(page);
        await page.goto("/#/admin/events");
        await waitForEventsLoaded(page);

        const searchTerm = `E2EFilterMatch${Date.now()}`;
        await page.getByPlaceholder("Search event payloads...").fill(searchTerm);
        // The filter reload (Search_changed -> Load_page) lands on the
        // zero-results empty state before either friend below exists.
        // Note: `paginationBar`'s own "No matches" string (for
        // `TotalMatches = 0`) is actually unreachable dead code — the
        // `List.isEmpty model.Events` branch in `view` always wins first for
        // a zero-match filter, rendering "No events found." instead. Filed
        // as administration-nf3wk rather than silently patched here.
        await expect(page.getByText("No events found.")).toBeVisible();

        await expect(followButton(page)).toBeVisible();
        await followButton(page).click();
        await expect(followingButton(page)).toBeVisible();

        const matchingName = `${searchTerm} Yes`;
        const nonMatchingName = `E2ENoMatch${Date.now()}`;
        const [matchingSlug] = await addFriends(request, baseURL!, [matchingName, nonMatchingName]);

        const matchingRow = friendRowLocator(page, matchingSlug);
        await expect(matchingRow.first()).toBeVisible({ timeout: 4_000 });

        // The non-matching friend's name never appears anywhere on the
        // filtered page — the server-side filter (buildFilterConditions)
        // excludes it from queryEventsAfter's result set entirely.
        await expect(page.getByText(nonMatchingName)).toHaveCount(0);
    });

    test("No orphan polling (a): toggling Follow off stops further getEventsAfter requests", async ({ page }) => {
        test.setTimeout(45_000);
        const tailRequests = trackTailRequests(page);
        await page.goto("/#/admin/events");
        await waitForEventsLoaded(page);

        await expect(followButton(page)).toBeVisible();
        await followButton(page).click();
        await expect(followingButton(page)).toBeVisible();

        // Let at least one poll cycle actually fire before we act, so the
        // "stopped" assertion below is meaningful (there was something to stop).
        await page.waitForTimeout(2_500);
        expect(tailRequests.length).toBeGreaterThan(0);

        await followingButton(page).click();
        await expect(followButton(page)).toBeVisible();

        // Grace period so any request already in flight at the moment of the
        // click (fired before the epoch bump landed in `update`) settles
        // before we take the baseline — the epoch guard drops its *response*,
        // not the already-sent request itself.
        await page.waitForTimeout(500);
        const baseline = tailRequests.length;

        await page.waitForTimeout(10_000);
        expect(tailRequests.length).toBe(baseline);
    });

    test("No orphan polling (b): paginating away from page 1 stops further getEventsAfter requests", async ({
        page,
        request,
        baseURL,
    }) => {
        test.setTimeout(60_000);
        const tailRequests = trackTailRequests(page);

        // Force HasMore = true deterministically (independent of whatever's
        // already in the real dev DB) by creating one more matching event
        // than the default page size (25) under a unique per-run filter term.
        const term = `E2EPaginate${Date.now()}`;
        await addFriends(
            request,
            baseURL!,
            Array.from({ length: 26 }, (_, i) => `${term} ${i}`)
        );

        await page.goto("/#/admin/events");
        await waitForEventsLoaded(page);
        await page.getByPlaceholder("Search event payloads...").fill(term);
        await expect(page.getByText("Showing 1-25 of 26")).toBeVisible();

        await expect(followButton(page)).toBeVisible();
        await followButton(page).click();
        await expect(followingButton(page)).toBeVisible();

        await page.waitForTimeout(2_500);
        expect(tailRequests.length).toBeGreaterThan(0);

        const nextButton = page.getByRole("button", { name: "Next" });
        await expect(nextButton).toBeEnabled();
        await nextButton.click();

        // Next_page unconditionally calls stopFollowing before it even checks
        // HasMore — the toggle reverting to "Follow" is the observable proof.
        await expect(followButton(page)).toBeVisible();

        await page.waitForTimeout(500);
        const baseline = tailRequests.length;

        await page.waitForTimeout(10_000);
        expect(tailRequests.length).toBe(baseline);
    });

    test("No orphan polling (c) [load-bearing]: real client-side navigation away from /admin stops further getEventsAfter requests", async ({
        page,
    }) => {
        test.setTimeout(45_000);
        const tailRequests = trackTailRequests(page);
        await page.goto("/#/admin/events");
        await waitForEventsLoaded(page);

        await expect(followButton(page)).toBeVisible();
        await followButton(page).click();
        await expect(followingButton(page)).toBeVisible();

        await page.waitForTimeout(2_500);
        expect(tailRequests.length).toBeGreaterThan(0);

        // Real client-side navigation via Feliz.Router — click the sidebar's
        // Dashboard link (its onClick calls e.preventDefault() then
        // Router.navigate, firing Url_changed), never page.reload() or a
        // full document load. This is the path that was fixed only by static
        // review at administration-mtf1f iteration 2 and has never had an
        // automated guard until this spec.
        await page.getByRole("link", { name: "Dashboard" }).click();
        await expect(page).toHaveURL(/#\/?$/);

        await page.waitForTimeout(500);
        const baseline = tailRequests.length;

        await page.waitForTimeout(10_000);
        expect(tailRequests.length).toBe(baseline);
    });
});
