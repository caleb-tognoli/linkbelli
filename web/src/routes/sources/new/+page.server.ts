import type { Paged, Playlist } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	const res = await locals.api('/api/v1/playlists?limit=100');
	const playlists = res.ok ? ((await res.json()) as Paged<Playlist>).items : [];
	return { playlists };
};
