import http from "node:http";
import type { AddressInfo } from "node:net";
import { test, expect } from "@playwright/test";
import { makeSolidPng } from "./pngFixture";

// books-r8cfn: `bookReadingPosterCard`'s root anchor used to carry no fixed
// width ("cursor-pointer group" only) — inside `posterScroller`'s flex row,
// each card's rendered width came from its own content (title length,
// cover image aspect ratio), producing the "small, larger, very large, small
// again" pattern the builder reported. It also cropped non-2:3 covers (e.g.
// Audible's square art) because `.poster-image` uses `object-fit: cover`.
//
// Verifier iteration 1 flagged that seeding every book with `CoverUrl: null`
// never exercised either named cause: with no image at all, width came from
// the icon-placeholder's own layout, not from an image, and the new
// `.poster-image--contain` path never rendered. This iteration seeds real
// covers — a square PNG and a 2:3 PNG — served from a Node http server this
// spec starts itself on loopback only, so `addBook`'s `CoverUrl` fetch never
// touches the network. See `pngFixture.ts` for how the PNGs are built by
// hand from bytes (no fixture files, no image-encoding dependency).
test("Books dashboard: Recently Added rail renders every card at the same size with mixed covers", async ({
    page,
    request,
    baseURL,
}) => {
    test.setTimeout(45_000);

    // A loopback-only static server for the two cover fixtures. The app
    // server (whatever host:port `baseURL` points at) fetches these itself
    // when `addBook` resolves `CoverUrl` — both processes stay on 127.0.0.1,
    // so this never reaches the real network.
    const squarePng = makeSolidPng(60, 60, [200, 120, 60]);
    const portraitPng = makeSolidPng(60, 90, [60, 120, 200]); // 2:3, matches the poster frame
    const fixtureServer = http.createServer((req, res) => {
        const body = req.url === "/portrait.png" ? portraitPng : squarePng;
        res.writeHead(200, { "Content-Type": "image/png", "Content-Length": body.length });
        res.end(body);
    });
    await new Promise<void>((resolve) => fixtureServer.listen(0, "127.0.0.1", resolve));
    const fixturePort = (fixtureServer.address() as AddressInfo).port;

    try {
        const stamp = Date.now();
        const books = [
            { title: `E2E Uniform ${stamp} Square`, coverUrl: `http://127.0.0.1:${fixturePort}/square.png` },
            {
                title: `E2E Uniform ${stamp} Portrait — a considerably longer title that, before books-r8cfn, would have stretched its own card wider than its neighbours`,
                coverUrl: `http://127.0.0.1:${fixturePort}/portrait.png`,
            },
            { title: `E2E Uniform ${stamp} NoCover`, coverUrl: null as string | null },
        ];

        const slugs: string[] = [];
        for (const book of books) {
            const addBookResponse = await request.post(`${baseURL}/api/IMediathecaApi/addBook`, {
                data: [
                    {
                        Title: book.title,
                        Authors: ["E2E Author"],
                        Year: 2024,
                        CoverUrl: book.coverUrl,
                        Subjects: [],
                        Format: "Print",
                        ExternalIds: [],
                        SkipDuplicateCheck: true,
                    },
                ],
            });
            expect(addBookResponse.ok()).toBeTruthy();
            // AddBookOutcome's `Book_added` case serializes as
            // `{"Book_added": "<slug>"}` (see book-detail-progress.spec.ts).
            const addBookBody = (await addBookResponse.json()) as { Ok?: { Book_added?: string } };
            const slug = addBookBody.Ok?.Book_added;
            expect(slug).toBeTruthy();
            slugs.push(slug!);
        }

        await page.setViewportSize({ width: 1280, height: 900 });
        await page.goto("/");
        await page.getByRole("tab", { name: "Books" }).click();

        const recentlyAddedCard = page.locator("#dashboard-collapsed-card-BooksAdded");
        await expect(recentlyAddedCard).toBeVisible();

        const [squareSlug, portraitSlug, noCoverSlug] = slugs;

        // `cardItemKey` (Dashboard/Views.fs) composes the flip key as
        // `"{card}-{slug}"` — `BooksAdded` is the `DashboardCard` case for
        // this rail.
        const squareTile = recentlyAddedCard.locator(`[data-flip-key="BooksAdded-${squareSlug}"]`);
        const portraitTile = recentlyAddedCard.locator(`[data-flip-key="BooksAdded-${portraitSlug}"]`);
        const noCoverTile = recentlyAddedCard.locator(`[data-flip-key="BooksAdded-${noCoverSlug}"]`);

        await expect(squareTile).toBeVisible();
        await expect(portraitTile).toBeVisible();
        await expect(noCoverTile).toBeVisible();

        const squareImg = squareTile.locator("img");
        const portraitImg = portraitTile.locator("img");
        await expect(squareImg).toBeVisible();
        await expect(portraitImg).toBeVisible();

        // Prove the covers actually decoded through the real pipeline (not
        // the icon placeholder, not a broken image) before trusting their
        // box sizes below.
        await expect
            .poll(() => squareImg.evaluate((img: HTMLImageElement) => img.naturalWidth))
            .toBe(60);
        await expect
            .poll(() => squareImg.evaluate((img: HTMLImageElement) => img.naturalHeight))
            .toBe(60);
        await expect
            .poll(() => portraitImg.evaluate((img: HTMLImageElement) => img.naturalWidth))
            .toBe(60);
        await expect
            .poll(() => portraitImg.evaluate((img: HTMLImageElement) => img.naturalHeight))
            .toBe(90);

        // The named fix: book covers render with `object-fit: contain`
        // (`.poster-image--contain`), never `cover` — the square cover's
        // sides must not be cropped.
        await expect(squareImg).toHaveCSS("object-fit", "contain");
        await expect(portraitImg).toHaveCSS("object-fit", "contain");

        // The other named fix: every card in the rail — square cover, 2:3
        // cover, and no cover at all — renders at the exact same size, the
        // fixed rail width class rather than each card's own content.
        const boxes = [
            (await squareTile.boundingBox())!,
            (await portraitTile.boundingBox())!,
            (await noCoverTile.boundingBox())!,
        ];
        for (const box of boxes) expect(box).not.toBeNull();

        const [first, ...rest] = boxes;
        for (const box of rest) {
            expect(Math.abs(box.width - first.width)).toBeLessThanOrEqual(1);
            expect(Math.abs(box.height - first.height)).toBeLessThanOrEqual(1);
        }
    } finally {
        await new Promise<void>((resolve) => fixtureServer.close(() => resolve()));
    }
});
