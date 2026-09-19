/**
 * The search page's filters, as they sit in its own URL.
 *
 * One shape for the three places that read or write them — the server load, the page's own
 * navigation, and "Load more" — because each used to spell the list out by hand, and "Load more"
 * had fallen behind: it forgot the kind, the reading time and the tags, so the second page of a
 * filtered search was the unfiltered library.
 */
export interface SearchFilters {
	q: string;
	host: string;
	status: string;
	/** Days, kept as a plain number in the URL so the link stays readable. */
	finished: string;
	broken: string;
	sort: string;
	kind: string;
	maxMinutes: string;
	itemTags: string[];
}

export const EMPTY_FILTERS: SearchFilters = {
	q: '',
	host: '',
	status: '',
	finished: '',
	broken: '',
	sort: '',
	kind: '',
	maxMinutes: '',
	itemTags: []
};

/** Reads the filters out of the page's URL. */
export function filtersFrom(params: URLSearchParams): SearchFilters {
	return {
		q: params.get('q') ?? '',
		host: params.get('host') ?? '',
		status: params.get('status') ?? '',
		finished: params.get('finished') ?? '',
		broken: params.get('broken') ?? '',
		sort: params.get('sort') ?? '',
		kind: params.get('kind') ?? '',
		maxMinutes: params.get('maxMinutes') ?? '',
		itemTags: params.getAll('itemTag')
	};
}

/** The page's own URL for a set of filters — what goes in the address bar and gets shared. */
export function pageQuery(filters: SearchFilters): URLSearchParams {
	const params = new URLSearchParams();
	for (const key of ['q', 'host', 'status', 'finished', 'broken', 'sort', 'kind', 'maxMinutes'] as const) {
		if (filters[key]) params.set(key, filters[key]);
	}
	for (const tag of filters.itemTags) params.append('itemTag', tag);
	return params;
}

/**
 * The API's query for a set of filters.
 *
 * `finished` is a day count in the page URL and an instant on the API, resolved against `now`
 * so a shared link means "the last week" whenever it is opened.
 */
export function apiQuery(
	filters: SearchFilters,
	options: { limit?: number; cursor?: string | null; now?: number } = {}
): URLSearchParams {
	const params = new URLSearchParams();
	if (filters.q) params.set('q', filters.q);
	if (filters.host) params.set('host', filters.host);
	if (filters.status) params.set('status', filters.status);
	if (filters.finished) {
		const days = Number(filters.finished);
		if (Number.isFinite(days) && days > 0) {
			const now = options.now ?? Date.now();
			params.set('finishedSince', new Date(now - days * 86_400_000).toISOString());
		}
	}
	if (filters.broken) params.set('broken', 'true');
	if (filters.kind) params.set('kind', filters.kind);
	if (filters.maxMinutes) params.set('maxMinutes', filters.maxMinutes);
	if (filters.sort) params.set('sort', filters.sort);
	for (const tag of filters.itemTags) params.append('itemTag', tag);
	params.set('limit', String(options.limit ?? 25));
	if (options.cursor) params.set('cursor', options.cursor);
	return params;
}

/**
 * Where the results come from: a saved search is run by id, so what runs is exactly what was
 * saved — including its later pages.
 */
export function resultsPath(
	filters: SearchFilters,
	savedId: string | null,
	options: { limit?: number; cursor?: string | null; now?: number } = {}
): string {
	if (savedId) {
		const params = new URLSearchParams({ limit: String(options.limit ?? 25) });
		if (options.cursor) params.set('cursor', options.cursor);
		return `/search/saved/${savedId}?${params}`;
	}
	return `/search?${apiQuery(filters, options)}`;
}

/** How many filters narrow the results — the single answer to "is anything filtered?". */
export function activeFilterCount(filters: SearchFilters): number {
	let count = 0;
	for (const key of ['q', 'host', 'status', 'finished', 'broken', 'sort', 'kind', 'maxMinutes'] as const) {
		if (filters[key]) count++;
	}
	return count + filters.itemTags.length;
}
