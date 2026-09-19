import { expect, test } from '@playwright/test';
import { addLink, newAccount, newPlaylist, signIn, sql } from './support/app';

/**
 * A playlist of twenty pictures, in the view that leads with them.
 *
 * Thumbnails once sat on the same rate limit as outbound fetches, which allowed ten a minute — so
 * a grid showed the first ten pictures and a row of placeholders after them, and nothing at unit
 * level could see it, because each request was fine on its own. This asks for all twenty the way
 * a browser does and checks every one came back.
 */

/** A small, stable image the API's thumbnail proxy can fetch from the public internet. */
const IMAGE = 'https://www.wikipedia.org/portal/wikipedia.org/assets/img/Wikipedia-logo-v2.png';

test('every thumbnail in a twenty-item grid is served, not just the first few', async ({ page }) => {
	const account = await newAccount('grid');
	const playlist = await newPlaylist(account, 'Pictures');
	const run = Date.now().toString(36);

	const added = [];
	for (let n = 0; n < 20; n++) {
		added.push(await addLink(account, playlist.id, `https://example.com/?e2e=${run}-${n}`));
	}

	// The one thing the API cannot be asked to set: a thumbnail address comes only from a page's
	// own meta tags. Written after enrichment has finished with these links, so nothing overwrites it.
	const ids = added.map((i) => `'${i.link.id}'`).join(',');
	sql(`UPDATE "Links" SET "ThumbnailUrl" = '${IMAGE}' WHERE "Id" IN (${ids})`);

	await signIn(page, account);

	const statuses: number[] = [];
	page.on('response', (res) => {
		if (res.url().includes('/api/v1/thumbnails/')) statuses.push(res.status());
	});

	await page.goto(`/playlists/${playlist.id}`);
	await page.getByRole('button', { name: 'Grid view' }).click();

	// The images load lazily, as they should: brought into view one by one, the way somebody
	// scrolling down the grid would, rather than the grid being made to fetch what is off screen.
	const images = page.locator('img[src*="/api/v1/thumbnails/"]');
	await expect(images).toHaveCount(20);
	for (const image of await images.all()) {
		await image.scrollIntoViewIfNeeded();
	}

	await expect.poll(() => statuses.length, { timeout: 30_000 }).toBeGreaterThanOrEqual(20);
	expect(statuses.filter((s) => s !== 200)).toEqual([]);

	// And drawn: a thumbnail that failed is swapped for a placeholder, so every one left on the
	// page has to have actually decoded.
	await expect(images).toHaveCount(20);
	for (const image of await images.all()) {
		await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
	}
});
