import { test, expect } from "@playwright/test";

// books-f33e2: the book detail page's manual "Update progress" popover is
// the always-available fallback ADR-0076 assumes when neither Audible nor
// Goodreads is linked — this spec drives it end-to-end against the real
// dev stack (ADR-0027), seeding a book hermetically via a direct `addBook`
// API call (no Open Library/Audible network dependency). Setting progress
// to 100% through the popover must promote the book's status to Finished
// server-side (`Books.decide`, ADR-0076 §5) — a rule this spec proves by
// reading the page's own status control after the refetch, not by asserting
// on the API response alone.
test("Book detail: setting progress to 100 via the popover finishes the book", async ({
    page,
    request,
    baseURL,
}) => {
    test.setTimeout(45_000);

    const title = `E2E Progress ${Date.now()}`;
    const addBookResponse = await request.post(`${baseURL}/api/IMediathecaApi/addBook`, {
        data: [
            {
                Title: title,
                Authors: ["E2E Author"],
                Year: 2024,
                CoverUrl: null,
                Subjects: [],
                Format: "Print",
                ExternalIds: [],
                SkipDuplicateCheck: true,
            },
        ],
    });
    expect(addBookResponse.ok()).toBeTruthy();
    // AddBookOutcome's `Book_added` case serializes as `{"Book_added": "<slug>"}`
    // — the same single-field-DU shape `AddGameOutcome.Created` uses
    // (confirmed empirically in game-detail-persistent-cards.spec.ts).
    const addBookBody = (await addBookResponse.json()) as { Ok?: { Book_added?: string } };
    const slug = addBookBody.Ok?.Book_added;
    expect(slug).toBeTruthy();

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`/#/books/${slug}`);

    // The hero renders the title once the page has loaded — confirms we're
    // past the loading spinner before interacting with the progress card.
    await expect(page.getByRole("heading", { name: title })).toBeVisible();

    // Status starts Backlog (no progress observed yet, addBook's default).
    await expect(page.getByText("Backlog", { exact: true })).toBeVisible();

    // First observation: 60%, backdated — so it sorts *behind* the 100%
    // observation that follows, letting the later "remove the newest
    // observation" step prove the bar re-derives from what's left rather
    // than just clearing.
    await page.getByRole("button", { name: "Update progress" }).click();
    await page.getByRole("spinbutton").fill("60");
    await page.locator('input[type="date"]').fill("2024-01-01");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("60%", { exact: true }).first()).toBeVisible({ timeout: 10_000 });

    // Second observation: 100%, today (the popover's own date default) —
    // the aggregate rule (a 100% observation finishes a non-Finished book,
    // ADR-0076 §5) must now be reflected in the status control's own label.
    await page.getByRole("button", { name: "Update progress" }).click();
    await page.getByRole("spinbutton").fill("100");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Finished", { exact: true })).toBeVisible({ timeout: 10_000 });
    // Two "100%" strings now exist on the page (the progress bar's own
    // figure and the new row this observation adds to the history list
    // below it) — `.first()` avoids Playwright's strict-mode ambiguity;
    // either one proves the refetch picked up the new observation.
    await expect(page.getByText("100%", { exact: true }).first()).toBeVisible();

    // Remove the newest (100%, today) observation from the history list —
    // sorted newest-first, so its remove control is the first one. The
    // remaining 60% observation (backdated) must become the bar's own
    // figure once the removal's refetch lands; status never reverts
    // (ADR-0076: removing the observation that finished a book doesn't
    // un-finish it — `Change_status` is the only way back).
    await page.getByTitle("Remove observation").first().click();
    await page.getByRole("button", { name: "Remove", exact: true }).click();
    await expect(page.getByText("60%", { exact: true }).first()).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("Finished", { exact: true })).toBeVisible();
});
