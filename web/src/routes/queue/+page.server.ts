import type { Paged, SearchHit } from '$lib/types';
import type { PageServerLoad } from './$types';

const EMPTY: Paged<SearchHit> = { items: [], nextCursor: null, total: 0 };

/**
 * One list answering "what now" across every playlist. Rated things first, then whatever has
 * been carried longest — a queue that leads with the newest arrival is how a backlog becomes
 * permanent.
 */
export const load: PageServerLoad = async ({ locals }) => {
	// Both at once: what is waiting, and what was put aside. Snoozed things have to be visible
	// somewhere or "not now" is a one-way door, which is how a feature stops being trusted.
	const [res, asideRes] = await Promise.all([
		locals.api('/api/v1/search?status=unwatched&sort=queue&limit=25'),
		locals.api('/api/v1/search?snoozed=true&limit=25')
	]);

	const queue = res.ok ? ((await res.json()) as Paged<SearchHit>) : EMPTY;
	const aside = asideRes.ok ? ((await asideRes.json()) as Paged<SearchHit>) : EMPTY;

	return { queue, aside };
};
