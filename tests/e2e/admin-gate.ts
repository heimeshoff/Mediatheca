import { expect, type Page } from "@playwright/test";

/** administration-danger-gate: the six administration sections on Settings
 * (Events, Projections, Health, Images, Jobs, Surgery) are no longer
 * rendered on arrival — a "type danger to reveal" box stands in front of
 * them, so an ordinary Settings visit can't put a destructive recovery
 * action one stray click away. Every spec that drives a section has to pass
 * that gate first.
 *
 * The gate is client-side model state only (`AdminUnlocked` in
 * `Pages/Settings/{Types,State}.fs`), reset by `Settings.State.init` on
 * every /settings visit — so this has to run after each `page.goto`, not
 * once per spec file. Idempotent: a no-op if the sections are already
 * showing (the gate input is unmounted once unlocked). */
export async function unlockAdminSections(page: Page) {
    const gate = page.locator("#settings-admin-unlock");
    const sections = page.locator("#settings-admin-events");

    // Whichever of the two the page settles on — the gate while still
    // locked, the first section once unlocked. Waiting on the pair rather
    // than on the gate alone keeps the helper safe to call twice.
    await expect(gate.or(sections).first()).toBeVisible({ timeout: 10_000 });

    if (await gate.isVisible()) {
        await gate.fill("danger");
        await expect(sections).toBeVisible();
        await expect(gate).toHaveCount(0);
    }
}
