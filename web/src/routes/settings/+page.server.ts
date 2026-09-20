import type { Backup } from '$lib/backups';
import type { NotificationPrefs } from '$lib/notifications';
import type { ApiKey, Quota, Usage, User } from '$lib/types';
import type { Webhook, WebhookEventInfo } from '$lib/webhooks';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, cookies }) => {
	// All of it here rather than three panels fetching their own after the page has painted:
	// Email, Backups and Webhooks each rendered the word "Looking…" for a moment first.
	const [meRes, quotaRes, keysRes, usageRes, notificationsRes, backupsRes, hooksRes, eventsRes] =
		await Promise.all([
			locals.api('/api/v1/me'),
			locals.api('/api/v1/me/quota'),
			locals.api('/api/v1/me/apikeys'),
			locals.api('/api/v1/me/usage'),
			locals.api('/api/v1/notifications'),
			locals.api('/api/v1/backups'),
			locals.api('/api/v1/me/webhooks'),
			locals.api('/api/v1/me/webhooks/events')
		]);

	const user = meRes.ok ? ((await meRes.json()) as User) : null;
	const quota = quotaRes.ok ? ((await quotaRes.json()) as Quota) : null;
	const apiKeys = keysRes.ok ? ((await keysRes.json()) as ApiKey[]) : [];
	const theme = (cookies.get('lb_theme') ?? 'system') as 'light' | 'dark' | 'system';

	const usage = usageRes.ok ? ((await usageRes.json()) as Usage) : null;

	// Null rather than a default where the request failed, so a panel knows to ask again itself
	// instead of showing somebody an empty list as though it were the answer.
	const notifications = notificationsRes.ok ? ((await notificationsRes.json()) as NotificationPrefs) : null;
	const backups = backupsRes.ok ? ((await backupsRes.json()) as Backup[]) : null;
	const webhooks = hooksRes.ok ? ((await hooksRes.json()) as Webhook[]) : null;
	const webhookEvents = eventsRes.ok ? ((await eventsRes.json()) as WebhookEventInfo[]) : null;

	return { user, quota, apiKeys, usage, theme, notifications, backups, webhooks, webhookEvents };
};
