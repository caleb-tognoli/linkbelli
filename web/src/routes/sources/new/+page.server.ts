import type { Paged, Playlist } from '$lib/types';
import type { PageServerLoad } from './$types';

/**
 * The playlists a new source could fill.
 *
 * Creating a source used to attach it to nothing: the thing the product is for — a list that
 * fills itself — needed a second, undiscoverable step on the source's own page afterwards. They
 * are loaded here rather than in the component so the picker is there in the first paint.
 */
export const load: PageServerLoad = async ({ locals, url }) => {
	const res = await locals.api('/api/v1/playlists?limit=100');
	const playlists = res.ok ? ((await res.json()) as Paged<Playlist>).items : [];

	return {
		playlists,
		// Set when the flow started from a playlist's own Sources panel, which already knows the
		// answer to "and where should this go".
		preselectedPlaylistId: url.searchParams.get('playlist')
	};
};
