import { fail, redirect } from '@sveltejs/kit';
import { createPlaylist } from '$lib/api/playlists';
import type { Paged, Playlist, SharedPlaylist, Usage, Visibility } from '$lib/types';
import type { Actions, PageServerLoad } from './$types';

const VISIBILITIES: Visibility[] = ['Private', 'Unlisted', 'Public'];

export const load: PageServerLoad = async ({ locals, url, parent }) => {
	// The layout already fetched who this is and their folder tree, for the sidebar. Asking for
	// both again made /me two requests per navigation and the folder list two as well.
	const { user, folders } = await parent();
	const activeTags = url.searchParams.getAll('tag');
	const qs = new URLSearchParams();
	for (const t of activeTags) qs.append('tag', t);
	// This is the "root" playlist view: only playlists not filed in a folder, alongside the folder
	// tree. Filtered by a tag it is a search instead, and a playlist filed away matches too.
	if (activeTags.length === 0) qs.set('unfiled', 'true');

	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const [plRes, sharedRes, usageRes] = await Promise.all([
		locals.api(`/api/v1/playlists?${qs}`),
		locals.api('/api/v1/me/shared'),
		locals.api('/api/v1/me/usage')
	]);

	const playlists = plRes.ok
		? ((await plRes.json()) as Paged<Playlist>)
		: { items: [], nextCursor: null };
	// Only top-level folders belong on the root; subfolders are browsed within a folder.
	const rootFolders = folders.filter((f) => f.parentId === null);

	// Kept apart from the caller's own playlists on purpose: someone else's list, shared with
	// you, is not one of yours.
	const shared = sharedRes.ok ? ((await sharedRes.json()) as SharedPlaylist[]) : [];

	// What the getting-started checklist reads. Nothing is derived from a stored flag, so it
	// cannot claim a step is done when it isn't — or the other way round.
	const usage = usageRes.ok ? ((await usageRes.json()) as Usage) : null;

	return {
		playlists,
		rootFolders,
		shared,
		activeTags,
		usage,
		onboardingDismissed: user?.onboardingDismissed ?? false
	};
};

export const actions: Actions = {
	create: async ({ request, locals }) => {
		const data = await request.formData();
		const name = String(data.get('name') ?? '').trim();
		const description = String(data.get('description') ?? '').trim();
		const visibility = String(data.get('visibility') ?? 'Private') as Visibility;
		const tags = String(data.get('tags') ?? '')
			.split(',')
			.map((t) => t.trim())
			.filter(Boolean);

		const values = { name, description, visibility, tags: tags.join(', ') };

		if (!name) {
			return fail(400, { ...values, error: 'Name is required.' });
		}
		if (!VISIBILITIES.includes(visibility)) {
			return fail(400, { ...values, error: 'Invalid visibility.' });
		}

		const res = await createPlaylist(locals.api, {
			name,
			description: description || undefined,
			visibility,
			tags
		});
		if (!res.ok) {
			return fail(res.status, { ...values, error: 'Could not create the playlist.' });
		}

		const created = (await res.json()) as { id: string };
		throw redirect(303, `/playlists/${created.id}`);
	}
};
