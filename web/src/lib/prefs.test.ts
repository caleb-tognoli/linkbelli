import { beforeEach, describe, expect, it } from 'vitest';
import { defaultPrefs, savePrefs } from './prefs';

/** Reads back what savePrefs wrote, the way the server load does. */
function storedPrefs(): Record<string, Record<string, unknown>> {
	const raw = document.cookie
		.split('; ')
		.find((row) => row.startsWith('pl_prefs='))
		?.split('=')[1];

	return raw ? JSON.parse(decodeURIComponent(raw)) : {};
}

function clearCookie() {
	document.cookie = 'pl_prefs=; path=/; max-age=0';
}

describe('playlist view preferences', () => {
	beforeEach(clearCookie);

	it('starts from a sane default', () => {
		expect(defaultPrefs()).toEqual({
			sort: 'position',
			source: null,
			status: null,
			showUrls: false,
			showThumbnails: true,
			viewMode: 'table'
		});
	});

	it('stores a preference against the playlist it belongs to', () => {
		savePrefs('playlist-1', { sort: 'date-desc' });

		expect(storedPrefs()['playlist-1'].sort).toBe('date-desc');
	});

	it('merges into what is already there rather than replacing it', () => {
		savePrefs('playlist-1', { sort: 'shuffle' });
		savePrefs('playlist-1', { showUrls: true });

		const stored = storedPrefs()['playlist-1'];
		expect(stored.sort).toBe('shuffle');
		expect(stored.showUrls).toBe(true);
	});

	it('keeps playlists separate', () => {
		savePrefs('playlist-1', { sort: 'date-asc' });
		savePrefs('playlist-2', { sort: 'score-desc' });

		expect(storedPrefs()['playlist-1'].sort).toBe('date-asc');
		expect(storedPrefs()['playlist-2'].sort).toBe('score-desc');
	});

	it('drops the oldest entries rather than growing without limit', () => {
		// The cookie rides on every request, so it cannot be allowed to grow forever.
		for (let n = 0; n < 60; n++) {
			savePrefs(`playlist-${n}`, { sort: 'date-desc' });
		}

		const stored = storedPrefs();
		expect(Object.keys(stored).length).toBeLessThanOrEqual(50);
		expect(stored['playlist-59']).toBeDefined();
		expect(stored['playlist-0']).toBeUndefined();
	});

	it('survives a corrupted cookie instead of throwing', () => {
		document.cookie = 'pl_prefs=not-json; path=/';

		savePrefs('playlist-1', { sort: 'shuffle' });

		expect(storedPrefs()['playlist-1'].sort).toBe('shuffle');
	});
});
