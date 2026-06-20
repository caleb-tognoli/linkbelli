import { error, redirect } from '@sveltejs/kit';
import type { AttachedSource, Paged, Playlist, PlaylistItem } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params, parent }) => {
	const { user } = await parent();
	const base = `/api/v1/public/playlists/${encodeURIComponent(params.username)}/${encodeURIComponent(params.slug)}`;

	const [playlistRes, itemsRes, sourcesRes] = await Promise.all([
		locals.api(base),
		locals.api(`${base}/items`),
		locals.api(`${base}/sources`)
	]);

	if (playlistRes.status === 404) throw error(404, 'Playlist not found');
	if (!playlistRes.ok) throw error(playlistRes.status, 'Failed to load playlist');

	const playlist = (await playlistRes.json()) as Playlist;

	// If the logged-in user owns this playlist, send them to their private view.
	if (user && user.username === params.username) {
		redirect(302, `/playlists/${playlist.id}`);
	}

	const items = itemsRes.ok
		? ((await itemsRes.json()) as Paged<PlaylistItem>)
		: { items: [], nextCursor: null };
	const attachedSources = sourcesRes.ok ? ((await sourcesRes.json()) as AttachedSource[]) : [];

	return { playlist, items, attachedSources, username: params.username, slug: params.slug };
};
