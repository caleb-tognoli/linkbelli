import type { Source } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const res = await locals.api('/api/v1/sources');
	const sources = res.ok ? ((await res.json()) as Source[]) : [];

	return { sources };
};
