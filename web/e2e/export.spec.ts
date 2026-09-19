import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { addLink, newAccount, newPlaylist, signIn } from './support/app';

/**
 * Taking everything out. The filename was one of the things the proxy used to drop: it forwarded
 * no Content-Disposition, so every export downloaded under a name made up from the URL.
 */
test('an export downloads as a dated file that holds the library', async ({ page }) => {
	const account = await newAccount('exporter');
	const playlist = await newPlaylist(account, 'Kept');
	await addLink(account, playlist.id, 'https://example.com/');
	await signIn(page, account);

	await page.goto('/profile');
	const [download] = await Promise.all([
		page.waitForEvent('download'),
		page.getByRole('link', { name: 'Everything (JSON)' }).click()
	]);

	expect(download.suggestedFilename()).toMatch(new RegExp(`^linkbelli-${account.username}-\\d{4}-\\d{2}-\\d{2}\\.json$`));

	const file = JSON.parse(await readFile((await download.path())!, 'utf8'));
	expect(file.username).toBe(account.username);
	expect(file.version).toBeGreaterThanOrEqual(3);
	expect(file.playlists.map((p: { name: string }) => p.name)).toContain('Kept');
});
