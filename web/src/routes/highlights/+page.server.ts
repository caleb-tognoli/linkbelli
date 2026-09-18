import type { HighlightWithSource } from '$lib/highlights';
import type { Paged } from '$lib/types';
import type { PageServerLoad } from './$types';

const EMPTY: Paged<HighlightWithSource> = { items: [], nextCursor: null };

export const load: PageServerLoad = async ({ locals }) => {
	const res = await locals.api('/api/v1/highlights?limit=50');

	return {
		highlights: res.ok ? ((await res.json()) as Paged<HighlightWithSource>) : EMPTY
	};
};
