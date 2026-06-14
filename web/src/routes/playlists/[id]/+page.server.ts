import { error } from '@sveltejs/kit';
import type { AttachedSource, Paged, Playlist, PlaylistItem, SourceSummary } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const { api } = locals;
	const [playlistRes, itemsRes, attachedRes, ownRes] = await Promise.all([
		api(`/api/v1/playlists/${params.id}`),
		api(`/api/v1/playlists/${params.id}/items`),
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

	return { playlist, items, attachedSources, ownSources };
};
