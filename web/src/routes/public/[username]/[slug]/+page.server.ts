import { error, redirect } from '@sveltejs/kit';
import type { AttachedSource, Paged, Playlist, PlaylistItem } from '$lib/types';
import type { PlaylistPrefs } from '$lib/prefs';
import type { PageServerLoad } from './$types';

const VALID_SORTS = new Set(['position', 'date-asc', 'date-desc', 'shuffle']);
const VALID_STATUSES = new Set(['All', 'Unwatched', 'Watched']);

export const load: PageServerLoad = async ({ locals, params, parent, cookies }) => {
	const { user } = await parent();
	const base = `/api/v1/public/playlists/${encodeURIComponent(params.username)}/${encodeURIComponent(params.slug)}`;

	// Playlist must be fetched first: redirect check needs its id, and prefs are keyed by id.
	const playlistRes = await locals.api(base);
	if (playlistRes.status === 404) throw error(404, 'Playlist not found');
	if (!playlistRes.ok) throw error(playlistRes.status, 'Failed to load playlist');
	const playlist = (await playlistRes.json()) as Playlist;

	if (user && user.username === params.username) {
		redirect(302, `/playlists/${playlist.id}`);
	}

	const prefs = readPrefsCookie(cookies.get('pl_prefs'), playlist.id);
	const itemsQuery = buildItemsQuery(prefs.sort, prefs.source);

	const [itemsRes, sourcesRes] = await Promise.all([
		locals.api(`${base}/items${itemsQuery}`),
		locals.api(`${base}/sources`)
	]);

	const items = itemsRes.ok
		? ((await itemsRes.json()) as Paged<PlaylistItem>)
		: { items: [], nextCursor: null };
	const attachedSources = sourcesRes.ok ? ((await sourcesRes.json()) as AttachedSource[]) : [];

	return { playlist, items, attachedSources, username: params.username, slug: params.slug, initialPrefs: prefs };
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
