import { error } from '@sveltejs/kit';
import type { AttachedSource, Paged, Playlist, PlaylistItem, SourceSummary } from '$lib/types';
import type { PlaylistPrefs } from '$lib/prefs';
import type { PageServerLoad } from './$types';

const VALID_SORTS = new Set(['position', 'date-asc', 'date-desc', 'shuffle']);
const VALID_STATUSES = new Set(['All', 'Unwatched', 'Watched']);

export const load: PageServerLoad = async ({ locals, params, cookies }) => {
	const { api } = locals;

	const prefs = readPrefsCookie(cookies.get('pl_prefs'), params.id);
	const itemsQuery = buildItemsQuery(prefs.sort, prefs.source);

	const [playlistRes, itemsRes, attachedRes, ownRes] = await Promise.all([
		api(`/api/v1/playlists/${params.id}`),
		api(`/api/v1/playlists/${params.id}/items${itemsQuery}`),
		api(`/api/v1/playlists/${params.id}/sources`),
		api('/api/v1/sources')
	]);

	if (playlistRes.status === 404) throw error(404, 'Playlist not found');
	if (!playlistRes.ok) throw error(playlistRes.status, 'Failed to load playlist');

	const playlist = (await playlistRes.json()) as Playlist;
	const items = itemsRes.ok
		? ((await itemsRes.json()) as Paged<PlaylistItem>)
		: { items: [], nextCursor: null };
	const attachedSources = attachedRes.ok ? ((await attachedRes.json()) as AttachedSource[]) : [];
	const ownSources = ownRes.ok ? ((await ownRes.json()) as SourceSummary[]) : [];

	return { playlist, items, attachedSources, ownSources, initialPrefs: prefs };
};

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
				showThumbnails: s.showThumbnails ?? true
			};
		} catch { /* fall through */ }
	}
	return { sort: 'position', source: null, status: null, showUrls: false, showThumbnails: true };
}

function buildItemsQuery(sort: string, source: string | null): string {
	const p = new URLSearchParams();
	if (sort !== 'position') p.set('sort', sort);
	if (source !== null) p.set('source', source);
	const qs = p.toString();
	return qs ? `?${qs}` : '';
}
