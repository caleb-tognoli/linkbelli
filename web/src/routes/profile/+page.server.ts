import type { ApiKey, Quota, Usage, User } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, cookies }) => {
	const [meRes, quotaRes, keysRes, usageRes] = await Promise.all([
		locals.api('/api/v1/me'),
		locals.api('/api/v1/me/quota'),
		locals.api('/api/v1/me/apikeys'),
		locals.api('/api/v1/me/usage')
	]);

	const user = meRes.ok ? ((await meRes.json()) as User) : null;
	const quota = quotaRes.ok ? ((await quotaRes.json()) as Quota) : null;
	const apiKeys = keysRes.ok ? ((await keysRes.json()) as ApiKey[]) : [];
	const theme = (cookies.get('lb_theme') ?? 'system') as 'light' | 'dark' | 'system';

	const usage = usageRes.ok ? ((await usageRes.json()) as Usage) : null;

	return { user, quota, apiKeys, usage, theme };
};
