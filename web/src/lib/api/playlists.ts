import type { Paged, Playlist, TagSummary, Visibility } from '$lib/types';

/** The server-side API caller exposed on `event.locals` by hooks.server.ts. */
export type ApiFetch = App.Locals['api'];

async function json<T>(res: Response, what: string): Promise<T> {
	if (!res.ok) throw new Error(`${what} failed (${res.status})`);
	return res.json() as Promise<T>;
}

export interface ListPlaylistsOptions {
	tag?: string;
	cursor?: string;
	limit?: number;
	/** Only playlists not filed in any folder (the home "root" view). */
	unfiled?: boolean;
}

export function listPlaylists(api: ApiFetch, opts: ListPlaylistsOptions = {}): Promise<Paged<Playlist>> {
	const qs = new URLSearchParams();
	if (opts.tag) qs.set('tag', opts.tag);
	if (opts.cursor) qs.set('cursor', opts.cursor);
	if (opts.limit) qs.set('limit', String(opts.limit));
	if (opts.unfiled) qs.set('unfiled', 'true');
	const suffix = qs.toString() ? `?${qs}` : '';
	return api(`/api/v1/playlists${suffix}`).then((r) => json<Paged<Playlist>>(r, 'List playlists'));
}

/**
 * Every playlist, for a picker that has to offer all of them.
 *
 * Two pages used to ask for `limit=200` and get a hundred, because the API clamped an
 * out-of-range limit instead of refusing it — so anybody with more than a hundred playlists had
 * the rest silently missing from the "move this to…" list, with nothing anywhere saying so. The
 * API now refuses that limit, and this walks the pages instead.
 *
 * Capped, because a picker is the wrong shape for a thousand entries anyway; past that the answer
 * is a search box, not a longer list.
 */
export async function listAllPlaylists(api: ApiFetch, maxPages = 10): Promise<Playlist[]> {
	const all: Playlist[] = [];
	let cursor: string | undefined;

	for (let page = 0; page < maxPages; page++) {
		const res = await listPlaylists(api, { limit: 100, cursor }).catch(() => null);
		if (!res) break;

		all.push(...res.items);
		if (!res.nextCursor) break;
		cursor = res.nextCursor;
	}

	return all;
}

export function listOwnTags(api: ApiFetch): Promise<TagSummary[]> {
	return api('/api/v1/tags').then((r) => json<TagSummary[]>(r, 'List tags'));
}

export interface CreatePlaylistBody {
	name: string;
	description?: string;
	visibility?: Visibility;
	tags?: string[];
}

/** Returns the raw Response so the caller can branch on status (e.g. validation). */
export function createPlaylist(api: ApiFetch, body: CreatePlaylistBody): Promise<Response> {
	return api('/api/v1/playlists', { method: 'POST', body: JSON.stringify(body) });
}

export function getPlaylist(api: ApiFetch, id: string): Promise<Response> {
	return api(`/api/v1/playlists/${id}`);
}
