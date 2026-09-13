import { error } from '@sveltejs/kit';
import { listAllPlaylists } from '$lib/api/playlists';
import type { Playlist, Source, SourceHealth, SourceRun } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const { api } = locals;
	const [sourceRes, runsRes, healthRes, playlists] = await Promise.all([
		api(`/api/v1/sources/${params.id}`),
		api(`/api/v1/sources/${params.id}/runs`),
		api(`/api/v1/sources/${params.id}/health`),
		listAllPlaylists(api).catch(() => [] as Playlist[])
	]);

	if (sourceRes.status === 404) throw error(404, 'Source not found');
	if (!sourceRes.ok) throw error(sourceRes.status, 'Failed to load source');

	const source = (await sourceRes.json()) as Source;
	const runs = runsRes.ok ? ((await runsRes.json()) as SourceRun[]) : [];
	// The summary is a nicety on a page that works without it; a failure here shouldn't 500 the
	// source itself.
	const health = healthRes.ok ? ((await healthRes.json()) as SourceHealth) : null;

	return { source, runs, health, playlists };
};
