import type { Paged, PublicPlaylistSummary } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, url }) => {
	const q = url.searchParams.get('q') ?? '';
	const activeTags = url.searchParams.getAll('tag');

	const qs = new URLSearchParams();
	if (q) qs.set('q', q);
	for (const t of activeTags) qs.append('tag', t);
	const suffix = qs.toString() ? `?${qs}` : '';

	const resultsRes = await locals.api(`/api/v1/public/playlists${suffix}`);
	const results = resultsRes.ok
		? ((await resultsRes.json()) as Paged<PublicPlaylistSummary>)
		: { items: [], nextCursor: null };

	return { results, q, activeTags };
};
