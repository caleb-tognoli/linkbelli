import { error } from '@sveltejs/kit';
import type { Paged, PublicPlaylistSummary, PublicProfile } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params, parent }) => {
	const encoded = encodeURIComponent(params.username);

	const [profileRes, playlistsRes] = await Promise.all([
		locals.api(`/api/v1/public/users/${encoded}`),
		locals.api(`/api/v1/public/users/${encoded}/playlists`)
	]);

	if (profileRes.status === 404) throw error(404, 'Profile not found');
	if (!profileRes.ok) throw error(profileRes.status, 'Failed to load profile');

	const profile = (await profileRes.json()) as PublicProfile;
	const playlists = playlistsRes.ok
		? ((await playlistsRes.json()) as Paged<PublicPlaylistSummary>)
		: { items: [], nextCursor: null };

	const { user } = await parent();

	return { profile, playlists, isLoggedIn: !!user, isSelf: user?.username === params.username };
};
