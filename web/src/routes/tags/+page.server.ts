import type { TagUsage } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const res = await locals.api('/api/v1/tags/usage');
	const tags = res.ok ? ((await res.json()) as TagUsage[]) : [];

	return { tags };
};
