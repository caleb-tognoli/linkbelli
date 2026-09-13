import { redirect } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import type { PlaylistRole } from '$lib/types';
import type { Actions, PageServerLoad } from './$types';

interface Preview {
	playlistName: string;
	invitedBy: string;
	role: PlaylistRole;
	expiresAt: string;
}

/**
 * What the link leads to, before anybody commits to it.
 *
 * Fetched without the session on purpose: the person opening this may have no account here yet,
 * and being asked to make one before being told what for is the wrong order.
 */
export const load: PageServerLoad = async ({ params, fetch, locals }) => {
	const res = await fetch(`${API_BASE}/api/v1/invites/${encodeURIComponent(params.token)}`);

	if (!res.ok) {
		return { invite: null, signedIn: locals.authenticated };
	}

	return { invite: (await res.json()) as Preview, signedIn: locals.authenticated };
};

export const actions: Actions = {
	/**
	 * Takes it up.
	 *
	 * A form action rather than a fetch from the browser, so it works with the session the
	 * SvelteKit server holds — and so somebody who has just signed in on the way here lands
	 * straight in the playlist rather than back on this page.
	 */
	default: async ({ params, locals }) => {
		const res = await locals.api(`/api/v1/invites/${encodeURIComponent(params.token)}/accept`, {
			method: 'POST'
		});

		if (!res.ok) {
			return { error: 'That invitation is no longer valid. Ask for a new link.' };
		}

		const accepted = (await res.json()) as { playlistId: string };
		throw redirect(303, `/playlists/${accepted.playlistId}`);
	}
};
