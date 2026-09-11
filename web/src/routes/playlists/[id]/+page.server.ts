import { error } from '@sveltejs/kit';
import type { AttachedSource, Paged, Playlist, PlaylistItem, SourceSummary } from '$lib/types';
import type { PlaylistPrefs } from '$lib/prefs';
import type { PageServerLoad } from './$types';

const VALID_SORTS = new Set(['position', 'date-asc', 'date-desc', 'shuffle']);
const VALID_STATUSES = new Set(['All', 'Unwatched', 'Watched']);

export const load: PageServerLoad = async ({ locals, params, cookies }) => {
	const { api } = locals;

	// The playlist is fetched first because it carries the account-saved view, and the view
	// decides what to ask for. The cookie is the fallback: it is all an anonymous reader has,
	// and it still holds the last local state if the account has none yet.
	const playlistRes = await api(`/api/v1/playlists/${params.id}`);
	if (playlistRes.status === 404) throw error(404, 'Playlist not found');
	if (!playlistRes.ok) throw error(playlistRes.status, 'Failed to load playlist');

	const playlist = (await playlistRes.json()) as Playlist;
	const prefs = resolvePrefs(playlist, cookies.get('pl_prefs'), params.id);
	const initialStatus = prefs.status ?? 'Unwatched';
	const itemsQuery = buildItemsQuery(prefs.sort, prefs.source, initialStatus);

	const [itemsRes, attachedRes, ownRes] = await Promise.all([
		api(`/api/v1/playlists/${params.id}/items${itemsQuery}`),
		api(`/api/v1/playlists/${params.id}/sources`),
		api('/api/v1/sources')
	]);

	const items = itemsRes.ok
		? ((await itemsRes.json()) as Paged<PlaylistItem>)
		: { items: [], nextCursor: null };
	const attachedSources = attachedRes.ok ? ((await attachedRes.json()) as AttachedSource[]) : [];
	const ownSources = ownRes.ok ? ((await ownRes.json()) as SourceSummary[]) : [];

	return { playlist, items, attachedSources, ownSources, initialPrefs: prefs };
};

/** The account's saved view when there is one, otherwise whatever this browser remembers. */
function resolvePrefs(playlist: Playlist, cookie: string | undefined, playlistId: string): PlaylistPrefs {
	const saved = playlist.view;
	if (saved) {
		return {
			sort: VALID_SORTS.has(saved.sort ?? '') ? (saved.sort ?? 'position') : 'position',
			source: saved.source ?? null,
			status: VALID_STATUSES.has(saved.status ?? '') ? (saved.status ?? null) : null,
			showUrls: saved.showUrls,
			showThumbnails: saved.showThumbnails,
			viewMode: saved.viewMode === 'grid' ? 'grid' : 'table'
		};
	}

	return readPrefsCookie(cookie, playlistId);
}

function readPrefsCookie(raw: string | undefined, playlistId: string): PlaylistPrefs {
	if (raw) {
		try {
			const map = JSON.parse(raw) as Record<string, Partial<PlaylistPrefs>>;
			const s = map[playlistId] ?? {};
			return {
				sort: VALID_SORTS.has(s.sort ?? '') ? (s.sort ?? 'position') : 'position',
				source: typeof s.source === 'string' ? s.source : null,
				status: VALID_STATUSES.has(s.status ?? '') ? (s.status ?? null) : null,
				showUrls: s.showUrls ?? false,
				showThumbnails: s.showThumbnails ?? true,
				viewMode: s.viewMode === 'grid' ? 'grid' : 'table'
			};
		} catch { /* fall through */ }
	}
	return {
		sort: 'position',
		source: null,
		status: null,
		showUrls: false,
		showThumbnails: true,
		viewMode: 'table'
	};
}

function buildItemsQuery(sort: string, source: string | null, status: string): string {
	const p = new URLSearchParams();
	if (sort !== 'position') p.set('sort', sort);
	if (source !== null) p.set('source', source);
	if (status !== 'All') p.set('status', status.toLowerCase());
	const qs = p.toString();
	return qs ? `?${qs}` : '';
}
