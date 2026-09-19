import { expect, test } from '@playwright/test';
import { items, newAccount, newPlaylist, signIn } from './support/app';

/**
 * Saving a link with no connection — the share sheet on a train.
 *
 * The queue is only worth having if nothing is lost on the way through it: kept while offline,
 * said to be kept rather than dressed up as saved, and sent by itself once the connection comes
 * back, because the person who shared it is not coming back to press a button.
 */
test('a link saved offline is kept, and arrives once the connection returns', async ({ page, context }) => {
	const account = await newAccount('offline');
	const playlist = await newPlaylist(account, 'Underground');
	await signIn(page, account);

	const url = `https://example.org/read-later-${Date.now()}`;
	await page.goto(`/save?url=${encodeURIComponent(url)}`);
	await expect(page.getByLabel('Address')).toHaveValue(url);

	await context.setOffline(true);
	await page.getByRole('button', { name: 'Save' }).click();

	await expect(page.getByText('Waiting for a connection.')).toBeVisible();
	expect(await items(account, playlist.id)).toEqual([]);

	// Back in signal. The page itself sends what it kept; nothing here is pressed.
	await context.setOffline(false);

	await expect
		.poll(async () => (await items(account, playlist.id)).map((i) => i.link.url), { timeout: 20_000 })
		.toContain(url);
});
