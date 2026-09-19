import type { Feed, Paged, Playlist, SearchHit, Usage } from '$lib/types';
import type { PageServerLoad } from './$types';

/**
 * The home page.
 *
 * A visitor gets the introduction. Somebody signed in used to get the same introduction — the
 * first thing after signing in was "What is in here" — so for them this loads what is theirs:
 * what is up next, the playlists touched most recently, and what the feed has brought.
 * Tolerant of transient failures: each part degrades to empty rather than failing the page.
 */
export const load: PageServerLoad = async ({ locals }) => {
	if (!locals.authenticated) return { dashboard: null };

	const [queueRes, playlistsRes, feedRes, usageRes] = await Promise.all([
		locals.api('/api/v1/search?status=unwatched&sort=queue&limit=5'),
		locals.api('/api/v1/playlists?limit=6'),
		locals.api('/api/v1/feed?limit=1'),
		locals.api('/api/v1/me/usage')
	]);

	return {
		dashboard: {
			upNext: queueRes.ok ? ((await queueRes.json()) as Paged<SearchHit>) : { items: [], nextCursor: null, total: 0 },
			recent: playlistsRes.ok ? ((await playlistsRes.json()) as Paged<Playlist>).items : [],
			feedNew: feedRes.ok ? ((await feedRes.json()) as Feed).newCount : 0,
			usage: usageRes.ok ? ((await usageRes.json()) as Usage) : null
		}
	};
};
