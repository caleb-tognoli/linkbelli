import { fail } from '@sveltejs/kit';
import type { Playlist, ImportResult } from '$lib/types';
import type { Actions, PageServerLoad } from './$types';
import { parseRows } from '$lib/importFormats';

export const load: PageServerLoad = async ({ locals }) => {
	// Load all the caller's playlists for the destination picker.
	const playlists: Playlist[] = [];
	let cursor: string | null = null;
	do {
		const qs = new URLSearchParams({ limit: '100' });
		if (cursor) qs.set('cursor', cursor);
		const res = await locals.api(`/api/v1/playlists?${qs}`);
		if (!res.ok) break;
		const page = (await res.json()) as { items: Playlist[]; nextCursor: string | null };
		playlists.push(...page.items);
		cursor = page.nextCursor;
	} while (cursor);

	return { playlists };
};

// --- Action ---

export const actions: Actions = {
	import: async ({ request, locals }) => {
		const data = await request.formData();
		const file = data.get('file') as File | null;
		const destination = String(data.get('destination') ?? 'none');
		const playlistId =
			destination === 'existing' ? String(data.get('playlistId') ?? '').trim() : undefined;
		const newPlaylistName =
			destination === 'new' ? String(data.get('newPlaylistName') ?? '').trim() : undefined;

		if (!file || file.size === 0) {
			return fail(400, { error: 'Choose a file to import.' });
		}
		if (destination === 'new' && !newPlaylistName) {
			return fail(400, { error: 'Please enter a name for the new playlist.' });
		}
		if (destination === 'existing' && !playlistId) {
			return fail(400, { error: 'Please select a playlist.' });
		}

		const text = await file.text();
		const rows = parseRows(text, file.name);

		if (rows.length > 2000) {
			return fail(400, {
				error: `Too many rows (${rows.length}). The limit is 2,000 per import — split your file and import in batches.`
			});
		}

		if (rows.length === 0) {
			return fail(400, {
				error:
					'Nothing to import from that file. A CSV needs a header row with a "url" column; ' +
					'a bookmark export or a plain list of addresses is read as it is.'
			});
		}

		const body: Record<string, unknown> = { rows };
		if (destination === 'existing' && playlistId) body.playlistId = playlistId;
		if (destination === 'new' && newPlaylistName) body.newPlaylistName = newPlaylistName;

		const res = await locals.api('/api/v1/import', {
			method: 'POST',
			body: JSON.stringify(body)
		});

		if (!res.ok) {
			const err = await res.json().catch(() => null);
			return fail(res.status, { error: (err as { title?: string })?.title ?? 'Import failed.' });
		}

		const result = (await res.json()) as ImportResult;
		return { success: true as const, result };
	}
};
