import { test, expect, type Page, type APIRequestContext } from "@playwright/test";
import { unlockAdminSections } from "./admin-gate";

// administration-svq3t (ADR-0027 harness, ADR-0034 guardrails): the Surgery
// tab's first Playwright coverage. administration-wwc36 shipped edit/delete/
// rename fully TDD'd server-side (EventSurgeryTests.fs/AdminSurgeryTests.fs)
// but left the client UI (three operation panels, paper-overlay confirm
// dialogs, the cross-tab "projections out of sync" banner) verified only via
// `npm run build` (Fable typecheck) and manual reasoning.
//
// THIS IS THE SUITE'S FIRST DESTRUCTIVE SPEC — edit/delete/rename mutate real
// `events` rows, and unlike the additive-only `addFriend` specs elsewhere in
// this suite, there is no restore path yet (administration-n8kqw, wipe-first
// import, is still open). ADR-0027's `reuseExistingServer: !process.env.CI`
// convenience path would otherwise let this spec fire real edit/delete/
// rename mutations at a developer's live dev DATA_DIR. The `test.skip` gate
// below is the guard against that — it must run before any test in this
// file, so the whole describe block is skipped (not individual tests) unless
// CI is set.

/** Fable.Remoting wraps even a single argument in a JSON array; response for
 * a `Result<string, string>`-returning method is `{"Ok": "..."}`. */
async function addFriend(request: APIRequestContext, baseURL: string, name: string): Promise<string> {
    const response = await request.post(`${baseURL}/api/IMediathecaApi/addFriend`, { data: [name] });
    expect(response.ok()).toBeTruthy();
    const body = (await response.json()) as { Ok?: string };
    expect(body.Ok).toBeTruthy();
    return body.Ok!;
}

type DiscoveredEvent = {
    GlobalPosition: number;
    StreamId: string;
    StreamPosition: number;
    EventType: string;
};

/** Wire shape as Fable.Remoting.Json's `FableJsonConverter` actually encodes
 * `int64` fields: a STRING with an explicit leading sign (`"+1"`, `"+0"`, not
 * a bare JSON number) — confirmed empirically against a live server, not
 * assumed. `Number("+0")`/`Number("+1")` normalize this correctly (JS's
 * `Number()` accepts a leading `+`), matching the plain-digit text the
 * server actually renders via `sprintf "%d"` (no sign) in the DOM. */
type RawEventDto = {
    GlobalPosition: string;
    StreamId: string;
    StreamPosition: string;
    EventType: string;
};

/** Discovers a seeded friend's `Friend_added` event via a direct
 * `POST /api/admin/getEventPage` call — the same admin route the Events tab
 * itself calls. `StreamFilter: "Friend-" + slug` scopes the query to exactly
 * this friend's own stream, so the match is exact regardless of ambient DB
 * size. `EventPageQuery`'s other `EventFilter` fields are `null` (Fable's
 * wire representation of `None`). */
async function findFriendAddedEvent(request: APIRequestContext, baseURL: string, slug: string): Promise<DiscoveredEvent> {
    const query = {
        Filter: {
            Search: null,
            StreamFilter: `Friend-${slug}`,
            EventTypeFilter: null,
            BoundedContext: null,
            TimestampFrom: null,
            TimestampTo: null,
        },
        Before: null,
        PageSize: 10,
    };
    const response = await request.post(`${baseURL}/api/admin/getEventPage`, { data: [query] });
    expect(response.ok()).toBeTruthy();
    const body = (await response.json()) as { Events: RawEventDto[] };
    const event = body.Events.find((e) => e.EventType === "Friend_added");
    if (!event) {
        throw new Error(`No Friend_added event found for stream Friend-${slug}`);
    }
    return {
        GlobalPosition: Number(event.GlobalPosition),
        StreamId: event.StreamId,
        StreamPosition: Number(event.StreamPosition),
        EventType: event.EventType,
    };
}

/** Every operation panel (`sectionCard` in AdminSurgery/Views.fs) is a
 * `.velvet-card` wrapping exactly one `h3` heading. Edit and Delete render
 * simultaneously and share byte-identical "Global position" labels,
 * `global_position` placeholders, and "Load" buttons via the shared
 * `globalPositionInput` helper — so every locator below is scoped to its own
 * panel by heading, never a bare page-level locator. */
function panelCard(page: Page, heading: string) {
    return page.locator(".velvet-card").filter({ has: page.getByRole("heading", { name: heading, exact: true }) });
}

/** The paper-overlay confirm dialog (ADR-0016, `Components.ModalPanel`) —
 * `.paper-overlay` is otherwise unused on the Admin/Surgery/Projections pages,
 * and only one `PendingAction` (hence only one dialog) can be open at a time. */
function confirmDialog(page: Page) {
    return page.locator(".paper-overlay");
}

/** administration-k3vmt: every former /admin tab is now an inline
 * collapsible section on Settings (`Pages/Settings/Views.fs`'s
 * `adminSectionCard`). Located by the outer wrapper's own DOM id
 * (`settings-admin-<name>` — also the dirty banner's scroll target for
 * Projections) rather than a class + heading filter: the Surgery section's
 * content nests three more `.velvet-card` panels (`AdminSurgery/Views.fs`'s
 * `sectionCard`, e.g. "Edit event") inside this wrapper, and the outer
 * wrapper deliberately does NOT reuse `.velvet-card` itself so `panelCard`
 * above stays unambiguous — see that class-choice comment in
 * `Pages/Settings/Views.fs`. */
function adminSectionCard(page: Page, sectionId: string) {
    return page.locator(`#${sectionId}`);
}

/** Sections are collapsed and unloaded by default (administration-k3vmt) —
 * every URL under old `/admin/*` now lands on the one Settings page with no
 * section pre-expanded, so specs must expand the section they need before
 * interacting with its content. Toggles the section's own controlled
 * checkbox (`prop.isChecked`/`prop.onChange`), the same element DaisyUI's
 * `collapse` idiom uses to drive open/closed — checking it fires this
 * section's lazy-load on first expand. Idempotent: a no-op if already open. */
async function expandAdminSection(page: Page, sectionId: string) {
    // administration-danger-gate: no section renders until the "type danger"
    // gate above them is passed — unlock first, then toggle.
    await unlockAdminSections(page);
    const checkbox = adminSectionCard(page, sectionId).locator('input[type="checkbox"]');
    if (!(await checkbox.isChecked())) {
        await checkbox.check();
    }
}

/** Cross-section "projections out of sync" banner (administration-wwc36,
 * moved by administration-k3vmt): rendered above all six administration
 * sections on Settings regardless of which are expanded/collapsed,
 * client-derived from `AdminProjectionsModel.Stats`'s `Lag` field. */
const dirtyBanner = (page: Page) => page.getByText(/Projections out of sync — rebuild/);

/** Matches both "Rebuild all" (idle/enabled) and "Rebuilding all..."
 * (mid-queue/disabled) — the per-row "Rebuild"/"Rebuilding..." buttons never
 * carry the " all" suffix, so this stays unambiguous on the Projections tab. */
const rebuildAllButton = (page: Page) => page.getByRole("button", { name: /^Rebuild(ing)? all(\.\.\.)?$/ });

test.describe("Surgery tab — edit/delete/rename + confirm dialogs + dirty banner (ADR-0034)", () => {
    // Destructive-spec safety gate (criterion 1) — see the file-header
    // comment. Applies to every test declared below in this describe block.
    test.skip(
        !process.env.CI,
        "Destructive spec: edit/delete/rename mutate real events rows. Only runs against a guaranteed-isolated cold-started server (set CI=1) — never against a possibly-reused dev server (ADR-0027's reuseExistingServer convenience path), since there is no restore path yet (administration-n8kqw)."
    );

    // Test order is deliberate: Edit -> Delete -> Rename (+ HTTP rename-back
    // cleanup) -> dirty-banner/Rebuild-all last, so the file ends with clean
    // projections and an unpolluted "Friend_added" event-type namespace for
    // whatever spec file runs next against this same shared server process.

    test("Edit flow: preview an event by global position, edit it, confirm, and the result banner reports one row applied", async ({
        page,
        request,
        baseURL,
    }) => {
        const slug = await addFriend(request, baseURL!, `E2E Surgery Edit ${Date.now()}`);
        const event = await findFriendAddedEvent(request, baseURL!, slug);

        await page.goto("/#/admin/surgery");
        await expandAdminSection(page, "settings-admin-surgery");

        const editCard = panelCard(page, "Edit event");
        await editCard.getByPlaceholder("global_position").fill(String(event.GlobalPosition));
        await editCard.getByRole("button", { name: /^Load$/ }).click();

        await expect(
            editCard.getByText(`${event.StreamId} @ Friend_added (stream position ${event.StreamPosition})`, { exact: true })
        ).toBeVisible();

        const editedData = `{"e2eEdited":true,"marker":"${Date.now()}"}`;
        // getByRole("textbox", { name: ..., exact: true }), not getByLabel:
        // the "Data"/"Metadata" <span> sits inside a wrapping <label> as a
        // sibling of the <textarea>, which Playwright's getByLabel does not
        // resolve to an association (it finds 0 elements even though the
        // accessibility tree reports the textbox's accessible name as
        // "Data") — getByRole's own accessible-name matching does resolve
        // it. exact: true is still load-bearing: without it, "Data"
        // substring-matches the sibling "Metadata" textbox too.
        await editCard.getByRole("textbox", { name: "Data", exact: true }).fill(editedData);
        await editCard.getByRole("button", { name: "Save edit..." }).click();

        const dialog = confirmDialog(page);
        await expect(dialog.getByRole("heading", { name: "Confirm edit", exact: true })).toBeVisible();
        await expect(dialog).toContainText(`Edit event ${event.GlobalPosition} on ${event.StreamId}`);
        await expect(dialog.locator("pre")).toContainText(editedData);

        await dialog.getByRole("button", { name: "Confirm edit", exact: true }).click();

        await expect(page.getByText(/^Applied — 1 row affected\. Backup: .+$/)).toBeVisible({ timeout: 10_000 });
    });

    test("Delete flow: preview the stream-position-gap warning, confirm the hard-delete dialog, and the result banner reports one row affected", async ({
        page,
        request,
        baseURL,
    }) => {
        const slug = await addFriend(request, baseURL!, `E2E Surgery Delete ${Date.now()}`);
        const event = await findFriendAddedEvent(request, baseURL!, slug);

        await page.goto("/#/admin/surgery");
        await expandAdminSection(page, "settings-admin-surgery");

        const deleteCard = panelCard(page, "Delete event");
        await deleteCard.getByPlaceholder("global_position").fill(String(event.GlobalPosition));
        await deleteCard.getByRole("button", { name: /^Load$/ }).click();

        const warning = deleteCard.locator("p.text-warning");
        await expect(warning).toContainText(`is currently at position ${event.StreamPosition}`);
        await expect(warning).toContainText(`permanent gap in ${event.StreamId}'s position sequence`);

        await deleteCard.getByRole("button", { name: "Delete..." }).click();

        const dialog = confirmDialog(page);
        await expect(dialog.getByRole("heading", { name: "Confirm delete", exact: true })).toBeVisible();
        await expect(dialog).toContainText(`Delete event ${event.GlobalPosition} (Friend_added) on ${event.StreamId}`);
        await expect(dialog).toContainText("hard delete");
        await expect(dialog).toContainText("no trash or undo");

        await dialog.getByRole("button", { name: "Confirm delete", exact: true }).click();

        await expect(page.getByText(/^Applied — 1 row affected\. Backup: .+$/)).toBeVisible({ timeout: 10_000 });
    });

    test("Rename flow: preview a store-wide rename, confirm, and rename back via a direct HTTP call (load-bearing cleanup)", async ({
        page,
        request,
        baseURL,
    }) => {
        // Fresh friend seeded right before previewing — the preview's Sample
        // is a bounded (20-row), oldest-first list, so a row seeded here
        // (right after Edit/Delete's own two Friend_added rows, one of which
        // Delete already removed) stays well inside that bound.
        const slug = await addFriend(request, baseURL!, `E2E Surgery Rename ${Date.now()}`);

        await page.goto("/#/admin/surgery");
        await expandAdminSection(page, "settings-admin-surgery");

        const disposableType = `Friend_added_e2e_disposable_${Date.now()}`;
        const renameCard = panelCard(page, "Rename event type");
        await renameCard.getByLabel("Old event type").fill("Friend_added");
        await renameCard.getByLabel("New event type").fill(disposableType);
        await renameCard.getByRole("button", { name: "Preview" }).click();

        const countText = renameCard.getByText(/^\d+ rows? at 'Friend_added'$/);
        await expect(countText).toBeVisible();
        const rawCount = (await countText.textContent())!;
        const count = parseInt(rawCount.match(/^(\d+)/)![1], 10);
        expect(count).toBeGreaterThanOrEqual(1);

        await expect(renameCard.getByRole("cell", { name: `Friend-${slug}`, exact: true })).toBeVisible();

        await renameCard.getByRole("button", { name: "Rename..." }).click();

        const dialog = confirmDialog(page);
        await expect(dialog.getByRole("heading", { name: "Confirm rename", exact: true })).toBeVisible();
        await dialog.getByRole("button", { name: "Confirm rename", exact: true }).click();

        await expect(page.getByText(/^Applied — \d+ rows? affected\. Backup: .+$/)).toBeVisible({ timeout: 10_000 });

        // Load-bearing cleanup, not hygiene: the rename UPDATE is store-wide,
        // so restoring the "Friend_added" namespace must not depend on
        // alphabetical spec-file ordering — whatever spec runs next in this
        // same shared server process (e.g. event-tail-follow.spec.ts's
        // addFriend calls) needs "Friend_added" to mean what it always has.
        const renameBackResponse = await request.post(`${baseURL}/api/admin/renameEventType`, {
            data: [disposableType, "Friend_added"],
        });
        expect(renameBackResponse.ok()).toBeTruthy();
    });

    test("Cross-tab dirty banner: a committed surgery mutation flips it on without navigating; Rebuild all clears it", async ({
        page,
        request,
        baseURL,
    }) => {
        // ADR-0034 rewinds every checkpoint to 0 on every commit, and this
        // suite's own Edit/Delete/Rename tests above already dirtied
        // projections without ever running Rebuild-all — Rebuild-all replays
        // every registered projection sequentially over SSE, so this whole
        // flow (baseline rebuild + one more mutation + a second rebuild)
        // needs a generous budget.
        test.setTimeout(60_000);

        // First, drive Rebuild-all to a known-clean baseline (banner absent)
        // — required because prior tests in this file already left
        // projections dirty.
        await page.goto("/#/admin/projections");
        await expandAdminSection(page, "settings-admin-projections");
        await expect(rebuildAllButton(page)).toBeEnabled({ timeout: 15_000 });
        await rebuildAllButton(page).click();
        await expect(rebuildAllButton(page)).toHaveText("Rebuild all", { timeout: 60_000 });
        await expect(dirtyBanner(page)).toHaveCount(0);

        // Move to the Surgery tab and confirm the clean baseline holds there
        // too (the banner is rendered above the tab bar on every tab).
        await page.goto("/#/admin/surgery");
        await expandAdminSection(page, "settings-admin-surgery");
        await expect(dirtyBanner(page)).toHaveCount(0);

        // Commit one fresh surgery mutation while staying on this page —
        // Admin.State's Surgery_msg handler dispatches a Projections Load
        // immediately after every committed mutation, so the banner should
        // react without any navigation.
        const slug = await addFriend(request, baseURL!, `E2E Surgery DirtyBanner ${Date.now()}`);
        const event = await findFriendAddedEvent(request, baseURL!, slug);

        const editCard = panelCard(page, "Edit event");
        await editCard.getByPlaceholder("global_position").fill(String(event.GlobalPosition));
        await editCard.getByRole("button", { name: /^Load$/ }).click();
        await expect(
            editCard.getByText(`${event.StreamId} @ Friend_added (stream position ${event.StreamPosition})`, { exact: true })
        ).toBeVisible();
        await editCard.getByRole("button", { name: "Save edit..." }).click();
        await confirmDialog(page).getByRole("button", { name: "Confirm edit", exact: true }).click();
        await expect(page.getByText(/^Applied — 1 row affected\. Backup: .+$/)).toBeVisible({ timeout: 10_000 });

        await expect(dirtyBanner(page)).toBeVisible({ timeout: 15_000 });

        // Follow the banner's own link — an in-page expand+scroll
        // (administration-k3vmt), not a navigation, since per-section
        // deep-linking is gone. Proven by DOM state (the Projections
        // section's own checkbox flips to checked and its content becomes
        // interactable) rather than a URL change: the hash stays exactly
        // where it already was (still on the Surgery section, per this
        // test's own `page.goto` above).
        await expect(page).toHaveURL(/#\/admin\/surgery$/);
        await page.getByRole("link", { name: "Go to Projections" }).click();
        await expect(page).toHaveURL(/#\/admin\/surgery$/);
        await expect(adminSectionCard(page, "settings-admin-projections").locator('input[type="checkbox"]')).toBeChecked();

        // Then clear it via Rebuild all — the completion signal is the
        // banner disappearing and the button's accessible name reverting to
        // exactly "Rebuild all"; there is no done-toast.
        await expect(rebuildAllButton(page)).toBeEnabled({ timeout: 15_000 });
        await rebuildAllButton(page).click();
        await expect(dirtyBanner(page)).toHaveCount(0, { timeout: 60_000 });
        await expect(rebuildAllButton(page)).toHaveText("Rebuild all", { timeout: 60_000 });
    });
});
