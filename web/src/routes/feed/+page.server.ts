import type { Feed, Following } from '$lib/types';
import type { PageServerLoad } from './$types';

const EMPTY: Feed = { items: [], nextCursor: null, newCount: 0, lastSeenAt: null };

export const load: PageServerLoad = async ({ locals }) => {
	const [feedRes, followingRes] = await Promise.all([
		locals.api('/api/v1/feed?limit=30'),
		locals.api('/api/v1/me/following')
	]);

	return {
		feed: feedRes.ok ? ((await feedRes.json()) as Feed) : EMPTY,
		following: followingRes.ok
			? ((await followingRes.json()) as Following)
			: { playlists: [], users: [] }
	};
};
