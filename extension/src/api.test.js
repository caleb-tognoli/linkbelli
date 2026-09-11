import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { findExisting, isConfigured, listPlaylists, loadSettings, savePage } from './api.js';

const SETTINGS = { serverUrl: 'https://linkbelli.test', apiKey: 'lb_test_key' };

/** Stands in for chrome.storage.sync, which only exists inside an extension. */
function stubChrome(stored = {}) {
	globalThis.chrome = {
		storage: { sync: { get: vi.fn().mockResolvedValue(stored), set: vi.fn().mockResolvedValue(undefined) } }
	};
}

function stubFetch(response) {
	globalThis.fetch = vi.fn().mockResolvedValue(response);
}

function jsonResponse(body, status = 200) {
	return { ok: status >= 200 && status < 300, status, json: async () => body };
}

beforeEach(() => stubChrome());
afterEach(() => vi.restoreAllMocks());

describe('settings', () => {
	it('reports whether there is enough to make a request', () => {
		expect(isConfigured(SETTINGS)).toBe(true);
		expect(isConfigured({ serverUrl: '', apiKey: 'k' })).toBe(false);
		expect(isConfigured({ serverUrl: 's', apiKey: '' })).toBe(false);
	});

	it('strips a trailing slash so paths do not double up', async () => {
		stubChrome({ serverUrl: 'https://linkbelli.test/', apiKey: 'k' });

		expect((await loadSettings()).serverUrl).toBe('https://linkbelli.test');
	});

	it('fills in empties rather than returning undefined', async () => {
		expect(await loadSettings()).toEqual({ serverUrl: '', apiKey: '', lastPlaylistId: '' });
	});
});

describe('requests', () => {
	it('authenticates with the API key header', async () => {
		stubFetch(jsonResponse({ items: [] }));

		await listPlaylists(SETTINGS);

		const [url, init] = fetch.mock.calls[0];
		expect(url).toBe('https://linkbelli.test/api/v1/playlists?limit=100');
		expect(init.headers['X-Api-Key']).toBe('lb_test_key');
	});

	it('refuses to call out before it is configured', async () => {
		stubFetch(jsonResponse({}));

		await expect(listPlaylists({ serverUrl: '', apiKey: '' })).rejects.toThrow(/server address/i);
		expect(fetch).not.toHaveBeenCalled();
	});

	it('returns the playlists', async () => {
		stubFetch(jsonResponse({ items: [{ id: '1', name: 'Reading' }] }));

		expect(await listPlaylists(SETTINGS)).toEqual([{ id: '1', name: 'Reading' }]);
	});

	it('copes with a response carrying no items', async () => {
		stubFetch(jsonResponse({}));

		expect(await listPlaylists(SETTINGS)).toEqual([]);
	});
});

describe('error messages', () => {
	it.each([
		[401, /rejected/i],
		[403, /rejected/i],
		[429, /too many/i],
		[500, /returned 500/]
	])('turns %i into something worth showing someone', async (status, expected) => {
		stubFetch(jsonResponse({}, status));

		await expect(listPlaylists(SETTINGS)).rejects.toThrow(expected);
	});

	it('says the server is unreachable rather than surfacing a fetch error', async () => {
		globalThis.fetch = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'));

		await expect(listPlaylists(SETTINGS)).rejects.toThrow(/could not reach/i);
	});
});

describe('saving a page', () => {
	it('posts the url and note to the chosen playlist', async () => {
		stubFetch(jsonResponse({}, 201));

		const result = await savePage(SETTINGS, 'playlist-1', 'https://example.com/a', 'read later');

		const [url, init] = fetch.mock.calls[0];
		expect(url).toBe('https://linkbelli.test/api/v1/playlists/playlist-1/items');
		expect(init.method).toBe('POST');
		expect(JSON.parse(init.body)).toEqual({ url: 'https://example.com/a', note: 'read later' });
		expect(result.alreadySaved).toBe(false);
	});

	it('sends no note rather than an empty one', async () => {
		stubFetch(jsonResponse({}, 201));

		await savePage(SETTINGS, 'playlist-1', 'https://example.com/a', '');

		expect(JSON.parse(fetch.mock.calls[0][1].body).note).toBeNull();
	});

	it('treats a conflict as already saved, not as a failure', async () => {
		// 409 is what the API returns for a link already in that playlist. That is a normal
		// outcome to report plainly, not an error to throw.
		stubFetch(jsonResponse({}, 409));

		expect(await savePage(SETTINGS, 'p', 'https://example.com/a', null)).toEqual({
			alreadySaved: true
		});
	});
});

describe('finding where a page is already saved', () => {
	it('asks search for the exact url', async () => {
		stubFetch(jsonResponse({ items: [] }));

		await findExisting(SETTINGS, 'https://example.com/a?b=1');

		expect(fetch.mock.calls[0][0]).toContain(
			`q=${encodeURIComponent('https://example.com/a?b=1')}`
		);
	});

	it('reduces hits to the playlists they are in', async () => {
		stubFetch(
			jsonResponse({
				items: [
					{ itemId: 'i1', playlistId: 'p1', playlistName: 'Reading', link: {} },
					{ itemId: 'i2', playlistId: 'p2', playlistName: 'Watching', link: {} }
				]
			})
		);

		expect(await findExisting(SETTINGS, 'https://example.com/a')).toEqual([
			{ playlistId: 'p1', playlistName: 'Reading' },
			{ playlistId: 'p2', playlistName: 'Watching' }
		]);
	});

	it('reports nothing when the page has not been saved', async () => {
		stubFetch(jsonResponse({ items: [] }));

		expect(await findExisting(SETTINGS, 'https://example.com/a')).toEqual([]);
	});
});
