import { expect, test } from '@playwright/test';
import { addLink, newAccount, newPlaylist } from './support/app';

/**
 * A published playlist, as somebody without an account sees it — and as a chat app or a social
 * site sees it when the link is pasted, which is from the page's meta tags and nothing else.
 */
test('a published playlist opens signed out, with a card to share it by', async ({ browser }) => {
	const account = await newAccount('publisher');
	const playlist = await newPlaylist(account, 'Canal reading list', 'Public');
	await addLink(account, playlist.id, 'https://example.com/');

	// A fresh context: no cookies, nobody signed in.
	const visitor = await browser.newContext();
	const page = await visitor.newPage();

	await page.goto(`/public/${account.username}/${playlist.slug}`);

	await expect(page.getByRole('heading', { name: 'Canal reading list' })).toBeVisible();
	await expect(page.getByText('Example Domain').first()).toBeVisible();

	await expect(page.locator('meta[property="og:title"]')).toHaveAttribute('content', 'Canal reading list');
	await expect(page.locator('meta[property="og:description"]')).toHaveAttribute('content', /.+/);
	await expect(page.locator('meta[property="og:url"]')).toHaveAttribute(
		'content',
		new RegExp(`/public/${account.username}/${playlist.slug}$`)
	);

	await visitor.close();
});

/** Private stays private: the same address signed out is a page that is not there. */
test('a private playlist is not there for somebody signed out', async ({ browser }) => {
	const account = await newAccount('private');
	const playlist = await newPlaylist(account, 'Nobody else', 'Private');

	const visitor = await browser.newContext();
	const page = await visitor.newPage();
	const res = await page.goto(`/public/${account.username}/${playlist.slug}`);

	expect(res?.status()).toBe(404);
	await visitor.close();
});
