export interface PlaylistPrefs {
	sort: string;
	source: string | null;
	status: string | null; // null = use component default
	showUrls: boolean;
	showThumbnails: boolean;
}

const COOKIE = 'pl_prefs';
const MAX_ENTRIES = 50;

export function savePrefs(playlistId: string, updates: Partial<PlaylistPrefs>): void {
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
	return { sort: 'position', source: null, status: null, showUrls: false, showThumbnails: true };
}
