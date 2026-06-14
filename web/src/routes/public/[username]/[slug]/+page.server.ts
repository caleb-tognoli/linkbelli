import { error } from '@sveltejs/kit';
import type { Paged, Playlist, PlaylistItem } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const base = `/api/v1/public/playlists/${encodeURIComponent(params.username)}/${encodeURIComponent(params.slug)}`;
	const [playlistRes, itemsRes] = await Promise.all([locals.api(base), locals.api(`${base}/items`)]);

	if (playlistRes.status === 404) throw error(404, 'Playlist not found');
	if (!playlistRes.ok) throw error(playlistRes.status, 'Failed to load playlist');

	const playlist = (await playlistRes.json()) as Playlist;
	const items = itemsRes.ok
		? ((await itemsRes.json()) as Paged<PlaylistItem>)
		: { items: [], nextCursor: null };

	return { playlist, items, username: params.username, slug: params.slug };
};
