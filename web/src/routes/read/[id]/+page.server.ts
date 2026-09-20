import { error } from '@sveltejs/kit';
import type { Highlight } from '$lib/highlights';
import type { LinkContent, Paged, PlaylistItem } from '$lib/types';
import { parseSettings } from '$lib/readerSettings.svelte';
import type { PageServerLoad } from './$types';

/**
 * How far into the playlist next/previous can reach.
 *
 * The reader is opened from a row, so what it needs is the rows either side of that one. Fetching
 * the whole of a two-thousand-item playlist to find two of them would be absurd, so this is a
 * window: past it, the keys do nothing and the buttons are not offered. Said here rather than
 * discovered, because a control that silently stops working is worse than one that is absent.
 *
 * The API's own page ceiling. This was 200 once, which the API now refuses outright rather than
 * quietly trimming — and since a failed lookup here only hides the buttons, next and previous
 * disappeared everywhere without a single error to say why.
 */
const NEIGHBOUR_WINDOW = 100;

export const load: PageServerLoad = async ({ locals, params, url, cookies }) => {
	const res = await locals.api(`/api/v1/links/${params.id}/content`);

	// 404 covers both "no such link" and "that page had no article in it" — from the reader's
	// side they are the same answer: there is nothing here to read.
	if (res.status === 404) throw error(404, 'No readable text was saved for that link');
	if (!res.ok) throw error(res.status, 'Failed to load the article');

	const content = (await res.json()) as LinkContent;
	const from = url.searchParams.get('from');

	const [highlights, neighbours, fromName] = await Promise.all([
		highlightsAsync(locals.api, params.id),
		neighboursAsync(locals.api, from, params.id),
		playlistNameAsync(locals.api, from)
	]);

	return {
		content,
		// Read here, like the theme: the reader's size, width and font settle before the first
		// paint instead of being corrected a moment after it.
		reader: parseSettings(cookies.get('lb_reader')),
		// Marks have to be on the page when it paints, not a moment later: text that changes
		// colour after you have started reading it is worse than text that never did.
		highlights,
		// Where the reader came from, so there is somewhere to go back to — and a name to say.
		from,
		fromName,
		...neighbours
	};
};

/**
 * The passages already marked in this article.
 *
 * An empty list on failure rather than an error: not being able to draw the marks is a worse
 * reason to refuse somebody the article than not having them.
 */
async function highlightsAsync(api: App.Locals['api'], linkId: string): Promise<Highlight[]> {
	const res = await api(`/api/v1/links/${linkId}/highlights`).catch(() => null);
	if (!res?.ok) return [];

	return (await res.json()) as Highlight[];
}

/** The name of the playlist the reader was opened from, for the way back to it. */
async function playlistNameAsync(api: App.Locals['api'], playlistId: string | null): Promise<string | null> {
	if (!playlistId) return null;
	const res = await api(`/api/v1/playlists/${playlistId}`).catch(() => null);
	if (!res?.ok) return null;

	return ((await res.json()) as { name: string }).name;
}

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
