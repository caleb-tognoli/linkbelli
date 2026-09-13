import { listAllPlaylists } from '$lib/api/playlists';
import type { AutomationRule, Playlist } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	// Tolerant of transient failures — degrade rather than 500 the page.
	const [rulesRes, playlists] = await Promise.all([
		locals.api('/api/v1/automations'),
		listAllPlaylists(locals.api).catch(() => [] as Playlist[])
	]);

	return {
		rules: rulesRes.ok ? ((await rulesRes.json()) as AutomationRule[]) : [],
		playlists
	};
};
