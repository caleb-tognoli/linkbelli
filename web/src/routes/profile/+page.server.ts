import type { ApiKey, Quota, User } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, cookies }) => {
	const [meRes, quotaRes, keysRes] = await Promise.all([
		locals.api('/api/v1/me'),
		locals.api('/api/v1/me/quota'),
		locals.api('/api/v1/me/apikeys')
	]);

	const user = meRes.ok ? ((await meRes.json()) as User) : null;
	const quota = quotaRes.ok ? ((await quotaRes.json()) as Quota) : null;
	const apiKeys = keysRes.ok ? ((await keysRes.json()) as ApiKey[]) : [];
	const theme = (cookies.get('lb_theme') ?? 'system') as 'light' | 'dark' | 'system';

	return { user, quota, apiKeys, theme };
};
