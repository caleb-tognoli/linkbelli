import type { LayoutServerLoad } from './$types';
import type { User } from '$lib/types';

export const load: LayoutServerLoad = async ({ locals }) => {
	if (!locals.authenticated) {
		return { user: null };
	}

	const res = await locals.api('/api/v1/me');
	if (!res.ok) {
		return { user: null };
	}

	const user = (await res.json()) as User;
	return { user };
};
