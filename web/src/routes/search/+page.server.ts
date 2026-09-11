import type { HostFacet, Paged, SearchHit } from '$lib/types';
import type { PageServerLoad } from './$types';

const EMPTY: Paged<SearchHit> = { items: [], nextCursor: null, total: 0 };

export const load: PageServerLoad = async ({ locals, url }) => {
	const q = url.searchParams.get('q') ?? '';
	const host = url.searchParams.get('host') ?? '';
	const status = url.searchParams.get('status') ?? '';

	const params = new URLSearchParams();
	if (q) params.set('q', q);
	if (host) params.set('host', host);
	if (status) params.set('status', status);
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
		results: resultsRes.ok ? ((await resultsRes.json()) as Paged<SearchHit>) : EMPTY,
		hosts: hostsRes.ok ? ((await hostsRes.json()) as HostFacet[]) : []
	};
};
