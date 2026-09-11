import { sharedUrl } from '$lib/share';
import type { Paged, Playlist } from '$lib/types';
import type { PageServerLoad } from './$types';

/**
 * The landing point for a bookmarklet and for the system share sheet. Both hand over a URL and
 * whatever title they have; the share sheet's field names vary, so all the usual ones are read.
 */
export const load: PageServerLoad = async ({ locals, url }) => {
	const params = url.searchParams;

	const shared = sharedUrl(params);
	const title = params.get('title') ?? '';

	const res = await locals.api('/api/v1/playlists?limit=100');
	const playlists = res.ok ? ((await res.json()) as Paged<Playlist>).items : [];

	return { url: shared, title, playlists };
};
