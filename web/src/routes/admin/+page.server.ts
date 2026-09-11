import { error } from '@sveltejs/kit';
import type { AdminOverview, AuditEntry, ContentReport, Paged } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	const [res, auditRes, reportsRes] = await Promise.all([
		locals.api('/api/v1/admin/overview'),
		locals.api('/api/v1/admin/audit?limit=25'),
		locals.api('/api/v1/admin/reports?limit=25')
	]);

	// The API is the authority on who may look; the nav link is only a convenience.
	if (res.status === 403 || res.status === 401) throw error(403, 'Admins only');
	if (!res.ok) throw error(res.status, 'Failed to load the overview');

	const audit = auditRes.ok ? ((await auditRes.json()) as Paged<AuditEntry>).items : [];
	const reports = reportsRes.ok ? ((await reportsRes.json()) as Paged<ContentReport>).items : [];

	return { overview: (await res.json()) as AdminOverview, audit, reports };
};
