import type { Paged, PublicPlaylistSummary, TagSummary } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, url }) => {
	const q = url.searchParams.get('q') ?? '';
	const activeTags = url.searchParams.getAll('tag');
	const sort = url.searchParams.get('sort') ?? '';

	const qs = new URLSearchParams();
	if (q) qs.set('q', q);
	for (const t of activeTags) qs.append('tag', t);
	if (sort) qs.set('sort', sort);
	const suffix = qs.toString() ? `?${qs}` : '';

	const [resultsRes, trendingRes] = await Promise.all([
		locals.api(`/api/v1/public/playlists${suffix}`),
		locals.api('/api/v1/public/tags/trending')
	]);

	const results = resultsRes.ok
		? ((await resultsRes.json()) as Paged<PublicPlaylistSummary>)
		: { items: [], nextCursor: null };

	// A quiet row of what is actually moving. Degrades to nothing rather than failing the page.
	const trending = trendingRes.ok ? ((await trendingRes.json()) as TagSummary[]).slice(0, 12) : [];

	return { results, q, activeTags, sort, trending };
};
