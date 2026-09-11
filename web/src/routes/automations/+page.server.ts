import type { AutomationRule, Paged, Playlist } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
	// Tolerant of transient failures — degrade rather than 500 the page.
	const [rulesRes, playlistsRes] = await Promise.all([
		locals.api('/api/v1/automations'),
		locals.api('/api/v1/playlists?limit=200')
	]);

	return {
		rules: rulesRes.ok ? ((await rulesRes.json()) as AutomationRule[]) : [],
		playlists: playlistsRes.ok ? ((await playlistsRes.json()) as Paged<Playlist>).items : []
	};
};
