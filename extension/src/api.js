/**
 * The Linkbelli API, as the extension talks to it.
 *
 * Authenticated with an API key rather than a session: the extension is a separate client, and a
 * scoped key can be revoked on its own without signing the person out of the web app.
 */

const SETTINGS_KEYS = ['serverUrl', 'apiKey', 'lastPlaylistId'];

/** Scopes the extension actually needs, shown on the options page so the key can be narrow. */
export const REQUIRED_SCOPES = ['playlists:read', 'playlists:write', 'links:write'];

export async function loadSettings() {
	const stored = await chrome.storage.sync.get(SETTINGS_KEYS);
	return {
		serverUrl: (stored.serverUrl ?? '').replace(/\/+$/, ''),
		apiKey: stored.apiKey ?? '',
		lastPlaylistId: stored.lastPlaylistId ?? ''
	};
}

export function saveSettings(values) {
	return chrome.storage.sync.set(values);
}

export function isConfigured(settings) {
	return !!settings.serverUrl && !!settings.apiKey;
}

/**
 * One request. Throws an Error whose message is worth showing someone — the popup has room for a
 * sentence, not a stack trace.
 */
async function request(settings, path, init = {}) {
	if (!isConfigured(settings)) {
		throw new Error('Set your server address and API key first.');
	}

	let response;
	try {
		response = await fetch(`${settings.serverUrl}/api/v1${path}`, {
			...init,
			headers: {
				'X-Api-Key': settings.apiKey,
				...(init.body ? { 'content-type': 'application/json' } : {}),
				...init.headers
			}
		});
	} catch {
		throw new Error('Could not reach your Linkbelli server.');
	}

	if (response.status === 401 || response.status === 403) {
		throw new Error('That API key was rejected. Check it on the options page.');
	}
	if (response.status === 429) {
		throw new Error('Too many requests just now — try again in a moment.');
	}
	if (!response.ok && response.status !== 409) {
		throw new Error(`The server returned ${response.status}.`);
	}

	return response;
}

export async function listPlaylists(settings) {
	const response = await request(settings, '/playlists?limit=100');
	const body = await response.json();
	return body.items ?? [];
}

/**
 * Saves a URL into a playlist. A 409 means it is already there, which is a normal outcome worth
 * reporting plainly rather than an error.
 */
export async function savePage(settings, playlistId, url, note) {
	const response = await request(settings, `/playlists/${playlistId}/items`, {
		method: 'POST',
		body: JSON.stringify({ url, note: note || null })
	});

	return { alreadySaved: response.status === 409 };
}

/**
 * Where this URL is already saved, if anywhere. Search canonicalizes a pasted URL and matches it
 * on the indexed dedup hash, so this is an exact lookup rather than a text scan.
 */
export async function findExisting(settings, url) {
	const response = await request(settings, `/search?q=${encodeURIComponent(url)}&limit=10`);
	const body = await response.json();

	return (body.items ?? []).map((hit) => ({
		playlistId: hit.playlistId,
		playlistName: hit.playlistName
	}));
}
