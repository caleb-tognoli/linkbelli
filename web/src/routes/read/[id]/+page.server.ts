import { error } from '@sveltejs/kit';
import type { LinkContent, Paged, PlaylistItem } from '$lib/types';
import type { PageServerLoad } from './$types';

/**
 * How far into the playlist next/previous can reach.
 *
 * The reader is opened from a row, so what it needs is the rows either side of that one. Fetching
 * the whole of a two-thousand-item playlist to find two of them would be absurd, so this is a
 * window: past it, the keys do nothing and the buttons are not offered. Said here rather than
 * discovered, because a control that silently stops working is worse than one that is absent.
 */
const NEIGHBOUR_WINDOW = 200;

export const load: PageServerLoad = async ({ locals, params, url }) => {
	const res = await locals.api(`/api/v1/links/${params.id}/content`);

	// 404 covers both "no such link" and "that page had no article in it" — from the reader's
	// side they are the same answer: there is nothing here to read.
	if (res.status === 404) throw error(404, 'No readable text was saved for that link');
	if (!res.ok) throw error(res.status, 'Failed to load the article');

	const content = (await res.json()) as LinkContent;

	return {
		content,
		// Where the reader came from, so Esc has somewhere to go back to.
		from: url.searchParams.get('from'),
		...(await neighboursAsync(locals.api, url.searchParams.get('from'), params.id))
	};
};

interface Neighbour {
	linkId: string;
	title: string;
}

/**
 * The items either side of this one in the playlist it was opened from.
 *
 * Null when it was not opened from a playlist — from search, from the feed, or from a link
 * somebody sent — because in those cases there is no "next" to mean anything.
 */
async function neighboursAsync(
	api: App.Locals['api'],
	playlistId: string | null,
	linkId: string
): Promise<{ previous: Neighbour | null; next: Neighbour | null }> {
	const none = { previous: null, next: null };
	if (!playlistId) return none;

	const res = await api(`/api/v1/playlists/${playlistId}/items?limit=${NEIGHBOUR_WINDOW}`).catch(
		() => null
	);
	if (!res?.ok) return none;

	const page = (await res.json()) as Paged<PlaylistItem>;
	const readable = page.items.filter((i) => i.link.wordCount);
	const here = readable.findIndex((i) => i.link.id === linkId);
	if (here < 0) return none;

	const at = (index: number): Neighbour | null => {
		const item = readable[index];
		return item ? { linkId: item.link.id, title: item.link.title ?? item.link.url } : null;
	};

	return { previous: at(here - 1), next: at(here + 1) };
}
