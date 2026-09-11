export interface PlaylistPrefs {
	sort: string;
	source: string | null;
	status: string | null; // null = use component default
	showUrls: boolean;
	showThumbnails: boolean;
	/** "table" or "grid". A list of videos or images wants a grid; a reading queue doesn't. */
	viewMode: string;
}

const COOKIE = 'pl_prefs';
const MAX_ENTRIES = 50;

/**
 * Persists how this playlist is being looked at. Signed-in people get it saved against their
 * account, so it follows them to another device; the cookie stays as the local mirror and as the
 * only option for someone reading a public playlist without an account.
 */
export function savePrefs(playlistId: string, updates: Partial<PlaylistPrefs>): void {
	saveCookie(playlistId, updates);

	// Fire-and-forget: a view preference is not worth blocking an interaction or reporting on.
	void fetch(`/api/v1/playlists/${playlistId}/view`, {
		method: 'PUT',
		headers: { 'content-type': 'application/json' },
		body: JSON.stringify(readCookie(playlistId))
	}).catch(() => {
		/* anonymous viewer, or offline — the cookie already has it */
	});
}

/** The current local view for a playlist, filled in from the defaults. */
function readCookie(playlistId: string): PlaylistPrefs {
	return { ...defaultPrefs(), ...(readMap()[playlistId] ?? {}) };
}

function saveCookie(playlistId: string, updates: Partial<PlaylistPrefs>): void {
	if (typeof document === 'undefined') return;
	const map = readMap();
	map[playlistId] = { ...defaultPrefs(), ...(map[playlistId] ?? {}), ...updates };
	const keys = Object.keys(map);
	if (keys.length > MAX_ENTRIES) {
		keys.slice(0, keys.length - MAX_ENTRIES).forEach((k) => delete map[k]);
	}
	document.cookie = `${COOKIE}=${encodeURIComponent(JSON.stringify(map))}; path=/; max-age=31536000; SameSite=Lax`;
}

function readMap(): Record<string, Partial<PlaylistPrefs>> {
	const raw = document.cookie.split('; ').find((r) => r.startsWith(`${COOKIE}=`))?.split('=')[1];
	if (!raw) return {};
	try {
		return JSON.parse(decodeURIComponent(raw));
	} catch {
		return {};
	}
}

export function defaultPrefs(): PlaylistPrefs {
	return {
		sort: 'position',
		source: null,
		status: null,
		showUrls: false,
		showThumbnails: true,
		viewMode: 'table'
	};
}
