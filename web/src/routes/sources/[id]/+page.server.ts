import { error } from '@sveltejs/kit';
import type { Source, SourceRun } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const { api } = locals;
	const [sourceRes, runsRes] = await Promise.all([
		api(`/api/v1/sources/${params.id}`),
		api(`/api/v1/sources/${params.id}/runs`)
	]);

	if (sourceRes.status === 404) throw error(404, 'Source not found');
	if (!sourceRes.ok) throw error(sourceRes.status, 'Failed to load source');

	const source = (await sourceRes.json()) as Source;
	const runs = runsRes.ok ? ((await runsRes.json()) as SourceRun[]) : [];

	return { source, runs };
};
