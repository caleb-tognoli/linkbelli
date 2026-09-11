import { error } from '@sveltejs/kit';
import type { SharedItem } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const res = await locals.api(`/api/v1/public/items/${encodeURIComponent(params.token)}`);

	// 404 covers a revoked share and one that never existed, deliberately — a dead share link
	// should not confirm that it once pointed at something.
	if (res.status === 404) throw error(404, 'That link has expired or was never shared');
	if (!res.ok) throw error(res.status, 'Failed to load that link');

	return { item: (await res.json()) as SharedItem };
};
