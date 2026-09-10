import type { Trash } from '$lib/types';
import type { PageServerLoad } from './$types';

const EMPTY: Trash = { playlists: [], items: [], retentionDays: 30 };

export const load: PageServerLoad = async ({ locals }) => {
	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const res = await locals.api('/api/v1/trash');
	const trash = res.ok ? ((await res.json()) as Trash) : EMPTY;

	return { trash };
};
