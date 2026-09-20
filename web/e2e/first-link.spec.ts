import { expect, test } from '@playwright/test';
import { PASSWORD, uniqueName } from './support/app';

/**
 * The first five minutes: an account, a playlist, a link, and the link turning into something
 * with a title. Every step through the browser, because this is the journey everyone takes and the
 * one that touches every layer — the form actions, the proxy, the API, enrichment going out to a
 * real page and back.
 */
test('a new account makes a playlist, pastes a link and sees it filled in', async ({ page }) => {
	const username = uniqueName('first');

	await page.goto('/register');
	await page.getByLabel('Username').fill(username);
	await page.getByLabel('Email').fill(`${username}@example.com`);
	// By role: the "Show password" button's name contains Password too.
	await page.getByRole('textbox', { name: 'Password', exact: true }).fill(PASSWORD);
	await page.getByRole('button', { name: 'Create account' }).click();
	await expect(page).not.toHaveURL(/\/register/);

	await page.goto('/playlists');
	// The page offers this twice on an empty library — in the header and in the empty state — and
	// either will do.
	await page.getByRole('button', { name: 'New playlist' }).first().click();
	const dialog = page.getByRole('dialog');
	await dialog.getByLabel('Name').fill('Things to read');
	await dialog.getByRole('button', { name: 'Create' }).click();
	await expect(page).toHaveURL(/\/playlists\/[0-9a-f-]{36}$/);

	const box = page.getByRole('textbox', { name: 'Search or add link' });
	await box.fill('https://example.com/');
	await box.press('Enter');

	// Enriched on the way in for a link added by hand, so the title is the page's own.
	await expect(page.getByRole('link', { name: 'Example Domain' }).first()).toBeVisible({ timeout: 20_000 });
	await expect(box).toHaveValue('');
});
