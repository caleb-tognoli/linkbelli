import { expect, test } from '@playwright/test';
import { addLink, newAccount, newPlaylist, signIn } from './support/app';

/**
 * A phone-width screen. The failure this catches is a page that scrolls sideways: one long
 * address or an unwrapped toolbar makes the whole page wider than the screen, and on a phone that
 * reads as the app being broken. Nothing short of a real layout engine can see it.
 */

test.use({ viewport: { width: 375, height: 812 } });

test('no page is wider than a phone screen', async ({ page }) => {
	const account = await newAccount('narrow');
	const playlist = await newPlaylist(account, 'A playlist with a rather long name to see whether it wraps');
	await addLink(account, playlist.id, 'https://example.com/');
	await signIn(page, account);

	const pages = [
		'/',
		'/playlists',
		`/playlists/${playlist.id}`,
		'/search',
		'/queue',
		'/highlights',
		'/sources',
		'/settings',
		'/save'
	];

	const tooWide: string[] = [];
	for (const path of pages) {
		await page.goto(path);
		await page.waitForLoadState('networkidle');

		const { scrollWidth, clientWidth } = await page.evaluate(() => ({
			scrollWidth: document.documentElement.scrollWidth,
			clientWidth: document.documentElement.clientWidth
		}));

		if (scrollWidth > clientWidth) tooWide.push(`${path}: ${scrollWidth}px on a ${clientWidth}px screen`);
	}

	expect(tooWide).toEqual([]);
});
