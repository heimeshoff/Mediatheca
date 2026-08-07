import { test, expect } from "@playwright/test";
import { unlockAdminSections } from "./admin-gate";

// administration-da908 (ADR-0027): the harness-proving smoke spec. Not a
// codification of ADR-0023's three live-tail behaviors — that's
// administration-a4d9b, which will sit alongside this file once the harness
// is proven. This spec only proves the harness can (a) drive the real dev
// stack, (b) trigger a real server-append via a direct Fable.Remoting call
// (not a raw event-store write, not UI clicking), and (c) observe both the
// DOM arrival and the underlying `getEventsAfter` network traffic.
test("Follow toggle: an event appended via a direct API call arrives live and getEventsAfter traffic is observable", async ({
    page,
    request,
    baseURL,
}) => {
    const tailRequests: string[] = [];
    page.on("request", (req) => {
        if (req.url().includes("/api/admin/getEventsAfter")) {
            tailRequests.push(req.url());
        }
    });

    // Hash-based routing (Elmish's Feliz.Router convention here) — the
    // real path lives after the `#`, confirmed against the rendered nav
    // links (`#/admin/events`) during this spike's dry run. administration-
    // k3vmt dissolved the /admin console into inline collapsible sections on
    // Settings — this URL still resolves (to Settings), but the Events
    // section starts collapsed and unloaded, so it has to be expanded before
    // the Follow button (inside it) is interactable — and, since
    // administration-danger-gate, only exists at all once the gate is passed.
    await page.goto("/#/admin/events");
    await unlockAdminSections(page);
    const eventsCheckbox = page.locator("#settings-admin-events").locator('input[type="checkbox"]');
    await eventsCheckbox.check();

    const followButton = page.getByRole("button", { name: /^Follow$/ });
    await expect(followButton).toBeVisible();
    await followButton.click();
    await expect(page.getByRole("button", { name: /^Following$/ })).toBeVisible();

    // Trigger "event appended elsewhere" via a direct Fable.Remoting API
    // call — addFriend needs no pre-existing entity and no external network
    // (unlike addMovie's TMDB dependency), so it's hermetic: no seeding
    // required for this spike's happy path. Fable.Remoting's wire protocol
    // wraps even a single argument in a JSON array (confirmed empirically
    // against the real server — see ADR-0027).
    const friendName = `E2E Smoke Friend ${Date.now()}`;
    const addFriendResponse = await request.post(
        `${baseURL}/api/IMediathecaApi/addFriend`,
        { data: [friendName] }
    );
    expect(addFriendResponse.ok()).toBeTruthy();
    const body = (await addFriendResponse.json()) as { Ok?: string };
    expect(body.Ok).toBeTruthy();
    const friendSlug = body.Ok!;

    // Poll cadence is ~2s (EventBrowser/State.fs's pollIntervalMs) — allow a
    // few cycles' worth of margin rather than a single exact-timed wait.
    const arrivedRow = page.locator("div", { hasText: `Friend-${friendSlug}` }).filter({ hasText: "Friend added" });
    await expect(arrivedRow.first()).toBeVisible({ timeout: 8_000 });

    expect(tailRequests.length).toBeGreaterThan(0);
});
