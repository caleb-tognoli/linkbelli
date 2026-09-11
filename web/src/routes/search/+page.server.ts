import type { HostFacet, Paged, SearchHit } from '$lib/types';
import type { PageServerLoad } from './$types';

const EMPTY: Paged<SearchHit> = { items: [], nextCursor: null, total: 0 };

export const load: PageServerLoad = async ({ locals, url }) => {
	const q = url.searchParams.get('q') ?? '';
	const host = url.searchParams.get('host') ?? '';
	const status = url.searchParams.get('status') ?? '';
	const finished = url.searchParams.get('finished') ?? '';
	const broken = url.searchParams.get('broken') ?? '';
	const sort = url.searchParams.get('sort') ?? '';
	const itemTags = url.searchParams.getAll('itemTag');

	const params = new URLSearchParams();
	if (q) params.set('q', q);
	if (host) params.set('host', host);
	if (status) params.set('status', status);
	// "finished" is a plain day count in the URL so the link stays readable and shareable; the
	// API takes the instant it resolves to.
	if (finished) {
		const days = Number(finished);
		if (Number.isFinite(days) && days > 0) {
			params.set('finishedSince', new Date(Date.now() - days * 86_400_000).toISOString());
		}
	}
	if (broken) params.set('broken', 'true');
	if (sort) params.set('sort', sort);
	for (const tag of itemTags) params.append('itemTag', tag);
	params.set('limit', '25');

	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const [resultsRes, hostsRes] = await Promise.all([
		locals.api(`/api/v1/search?${params}`),
		locals.api('/api/v1/search/hosts')
	]);

	return {
		q,
		host,
		status,
		finished,
		broken,
		sort,
		itemTags,
		results: resultsRes.ok ? ((await resultsRes.json()) as Paged<SearchHit>) : EMPTY,
		hosts: hostsRes.ok ? ((await hostsRes.json()) as HostFacet[]) : []
	};
};
