import type { HostFacet, Paged, SavedSearch, SearchHit } from '$lib/types';
import type { PageServerLoad } from './$types';
import { filtersFrom, resultsPath } from '$lib/searchParams';

const EMPTY: Paged<SearchHit> = { items: [], nextCursor: null, total: 0 };

export const load: PageServerLoad = async ({ locals, url }) => {
	const filters = filtersFrom(url.searchParams);

	// Opened from the sidebar. The saved search is run by id rather than unpacked into query
	// parameters, so what runs is exactly what was saved — including anything added to saved
	// searches later that the unpacking here has not learned about yet.
	const savedId = url.searchParams.get('saved');

	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const [resultsRes, hostsRes, savedRes] = await Promise.all([
		locals.api(`/api/v1${resultsPath(filters, savedId)}`),
		locals.api('/api/v1/search/hosts'),
		locals.api('/api/v1/search/saved')
	]);

	return {
		savedId,
		...filters,
		results: resultsRes.ok ? ((await resultsRes.json()) as Paged<SearchHit>) : EMPTY,
		hosts: hostsRes.ok ? ((await hostsRes.json()) as HostFacet[]) : [],
		saved: savedRes.ok ? ((await savedRes.json()) as SavedSearch[]) : []
	};
};
