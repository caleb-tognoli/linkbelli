import { error } from '@sveltejs/kit';
import type { Paged, Playlist, PlaylistItem } from '$lib/types';
import type { PageServerLoad } from './$types';

/** Items shown in an embed. It is a card on someone else's page, not the whole archive. */
const EMBED_LIMIT = 10;

export const load: PageServerLoad = async ({ locals, params, url }) => {
	const base = `/api/v1/public/playlists/${encodeURIComponent(params.username)}/${encodeURIComponent(params.slug)}`;

	const playlistRes = await locals.api(base);
	if (playlistRes.status === 404) throw error(404, 'Playlist not found');
	if (!playlistRes.ok) throw error(playlistRes.status, 'Failed to load playlist');

	const playlist = (await playlistRes.json()) as Playlist;

	const itemsRes = await locals.api(`${base}/items?limit=${EMBED_LIMIT}&sort=date-desc`);
	const items = itemsRes.ok
		? ((await itemsRes.json()) as Paged<PlaylistItem>)
		: { items: [], nextCursor: null };

	return {
		playlist,
		items,
		username: params.username,
		slug: params.slug,
		// An embed sits on a page with its own colours; the host can ask for the one that fits.
		theme: url.searchParams.get('theme') === 'dark' ? 'dark' : 'light'
	};
};
