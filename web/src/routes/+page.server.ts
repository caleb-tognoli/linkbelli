import { fail, redirect } from '@sveltejs/kit';
import { createPlaylist } from '$lib/api/playlists';
import type { Paged, Playlist, Source, TagSummary, Visibility } from '$lib/types';
import type { Actions, PageServerLoad } from './$types';

const VISIBILITIES: Visibility[] = ['Private', 'Unlisted', 'Public'];

export const load: PageServerLoad = async ({ locals, url }) => {
	const tag = url.searchParams.get('tag') ?? undefined;
	const playlistsPath = `/api/v1/playlists${tag ? `?tag=${encodeURIComponent(tag)}` : ''}`;

	// Tolerant of transient failures (e.g. rate limiting) — degrade rather than 500 the page.
	const [plRes, tagRes, srcRes] = await Promise.all([
		locals.api(playlistsPath),
		locals.api('/api/v1/tags'),
		locals.api('/api/v1/sources')
	]);

	const playlists = plRes.ok
		? ((await plRes.json()) as Paged<Playlist>)
		: { items: [], nextCursor: null };
	const tags = tagRes.ok ? ((await tagRes.json()) as TagSummary[]) : [];
	const sources = srcRes.ok ? ((await srcRes.json()) as Source[]) : [];

	return { playlists, tags, sources, activeTag: tag ?? null };
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
