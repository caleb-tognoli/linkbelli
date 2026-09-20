import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { expect, type Page } from '@playwright/test';

/**
 * Setting the scene for a journey.
 *
 * Only the step a journey is about goes through the browser. Everything it merely needs — an
 * account, a playlist, some links — is made through the API directly, so a journey about exporting
 * does not fail because the playlist screen changed.
 */

export const API = process.env.E2E_API_URL ?? 'http://localhost:5180';
export const PASSWORD = 'Passw0rd!e2e';

export interface Account {
	username: string;
	token: string;
}

export function uniqueName(prefix: string): string {
	return `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

async function call<T>(path: string, init: RequestInit & { token?: string } = {}): Promise<T> {
	const headers = new Headers(init.headers);
	headers.set('content-type', 'application/json');
	if (init.token) headers.set('authorization', `Bearer ${init.token}`);

	const res = await fetch(`${API}/api/v1${path}`, { ...init, headers });
	if (!res.ok) throw new Error(`${init.method ?? 'GET'} ${path} answered ${res.status}: ${await res.text()}`);

	// Some endpoints answer with nothing at all — registering does.
	const text = await res.text();
	return (text ? JSON.parse(text) : undefined) as T;
}

export async function newAccount(prefix = 'e2e'): Promise<Account> {
	const username = uniqueName(prefix);
	await call('/auth/register', {
		method: 'POST',
		body: JSON.stringify({ username, email: `${username}@example.com`, password: PASSWORD })
	});

	const { accessToken } = await call<{ accessToken: string }>('/auth/login', {
		method: 'POST',
		body: JSON.stringify({ login: username, password: PASSWORD })
	});

	return { username, token: accessToken };
}

export async function newPlaylist(
	account: Account,
	name: string,
	visibility: 'Private' | 'Unlisted' | 'Public' = 'Private'
): Promise<{ id: string; slug: string }> {
	return call('/playlists', {
		method: 'POST',
		token: account.token,
		body: JSON.stringify({ name, visibility })
	});
}

export async function addLink(account: Account, playlistId: string, url: string): Promise<{ id: string; link: { id: string } }> {
	return call(`/playlists/${playlistId}/items`, {
		method: 'POST',
		token: account.token,
		body: JSON.stringify({ url })
	});
}

export async function items(account: Account, playlistId: string): Promise<{ link: { url: string; title: string | null } }[]> {
	const page = await call<{ items: { link: { url: string; title: string | null } }[] }>(
		`/playlists/${playlistId}/items?limit=100`,
		{ token: account.token }
	);
	return page.items;
}

/** Signs in through the real form, so the browser holds the same cookies a person's would. */
export async function signIn(page: Page, account: Account): Promise<void> {
	await page.goto('/login');
	await page.getByRole('textbox', { name: /username or email/i }).fill(account.username);
	// By role: getByLabel matches a substring, and the field's own "Show password" button carries
	// Password in its name as well, so the plain label locator finds two things.
	await page.getByRole('textbox', { name: 'Password', exact: true }).fill(PASSWORD);
	await page.getByRole('button', { name: /sign in/i }).click();
	await expect(page).not.toHaveURL(/\/login/);
}

/**
 * Runs SQL against the stack's database.
 *
 * For the one kind of scene the API cannot set: a link's thumbnail address is only ever written by
 * enrichment, from whatever the page says. Kept to that — nothing a journey is actually about is
 * made this way.
 */
export function sql(statement: string): void {
	execFileSync(
		'docker',
		['compose', 'exec', '-T', 'postgres', 'psql', '-U', 'linkbelli', '-d', 'linkbelli', '-v', 'ON_ERROR_STOP=1', '-q', '-c', statement],
		{ cwd: resolve(import.meta.dirname, '../../..'), stdio: 'pipe' }
	);
}
