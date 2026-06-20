import { fail } from '@sveltejs/kit';
import type { Playlist, ImportResult } from '$lib/types';
import type { Actions, PageServerLoad } from './$types';

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

// --- CSV parsing ---

function parseLine(line: string): string[] {
	const result: string[] = [];
	let current = '';
	let inQuotes = false;

	for (let i = 0; i < line.length; i++) {
		const c = line[i];
		if (c === '"') {
			if (inQuotes && line[i + 1] === '"') {
				current += '"';
				i++;
			} else {
				inQuotes = !inQuotes;
			}
		} else if (c === ',' && !inQuotes) {
			result.push(current);
			current = '';
		} else {
			current += c;
		}
	}
	result.push(current);
	return result;
}

function parseCsv(text: string): Array<{ url: string; note?: string }> {
	const lines = text.split(/\r?\n/).filter((l) => l.trim());
	if (lines.length < 2) return [];

	const headers = parseLine(lines[0]).map((h) => h.toLowerCase().trim());
	const urlIdx = headers.indexOf('url');
	if (urlIdx === -1) return [];
	const noteIdx = headers.indexOf('note');

	return lines
		.slice(1)
		.map((line) => parseLine(line))
		.filter((cols) => cols[urlIdx]?.trim())
		.map((cols) => ({
			url: cols[urlIdx].trim(),
			...(noteIdx >= 0 && cols[noteIdx]?.trim() ? { note: cols[noteIdx].trim() } : {})
		}));
}

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
			return fail(400, { error: 'Please select a CSV file.' });
		}
		if (destination === 'new' && !newPlaylistName) {
			return fail(400, { error: 'Please enter a name for the new playlist.' });
		}
		if (destination === 'existing' && !playlistId) {
			return fail(400, { error: 'Please select a playlist.' });
		}

		const text = await file.text();
		const rows = parseCsv(text);

		if (rows.length === 0) {
			return fail(400, {
				error: 'No valid rows found. Make sure the file has a header row with a "url" column.'
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
