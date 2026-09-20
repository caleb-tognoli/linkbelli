import type { Paged, Playlist, SourceTemplate } from '$lib/types';
import type { PageServerLoad } from './$types';

/**
 * The playlists a new source could fill.
 *
 * Creating a source used to attach it to nothing: the thing the product is for — a list that
 * fills itself — needed a second, undiscoverable step on the source's own page afterwards. They
 * are loaded here rather than in the component so the picker is there in the first paint.
 */
export const load: PageServerLoad = async ({ locals, url }) => {
	const [res, templatesRes] = await Promise.all([
		locals.api('/api/v1/playlists?limit=100'),
		// With the page rather than after it: this is the first thing the page shows, and it
		// showed "Loading templates…" every time instead.
		locals.api('/api/v1/sources/templates')
	]);
	const playlists = res.ok ? ((await res.json()) as Paged<Playlist>).items : [];
	const templates = templatesRes.ok ? ((await templatesRes.json()) as SourceTemplate[]) : null;

	return {
		playlists,
		templates,
		// Set when the flow started from a playlist's own Sources panel, which already knows the
		// answer to "and where should this go".
		preselectedPlaylistId: url.searchParams.get('playlist')
	};
};
