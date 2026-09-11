import { error } from '@sveltejs/kit';
import type { AdminOverview } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	const res = await locals.api('/api/v1/admin/overview');

	// The API is the authority on who may look; the nav link is only a convenience.
	if (res.status === 403 || res.status === 401) throw error(403, 'Admins only');
	if (!res.ok) throw error(res.status, 'Failed to load the overview');

	return { overview: (await res.json()) as AdminOverview };
};
