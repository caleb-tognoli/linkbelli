import { error, json } from '@sveltejs/kit';
import type { Playlist } from '$lib/types';
import type { RequestHandler } from './$types';

const DEFAULT_WIDTH = 500;
const DEFAULT_HEIGHT = 420;

/**
 * oEmbed discovery, so pasting a public playlist address into anything that speaks oEmbed gets a
 * card rather than a bare link.
 */
export const GET: RequestHandler = async ({ locals, url, setHeaders }) => {
	const target = url.searchParams.get('url');
	if (!target) throw error(400, 'A url parameter is required');

	const parsed = parsePlaylistUrl(target, url.origin);
	if (!parsed) throw error(404, 'Not a playlist address on this server');

	const res = await locals.api(
		`/api/v1/public/playlists/${encodeURIComponent(parsed.username)}/${encodeURIComponent(parsed.slug)}`
	);
	if (res.status === 404) throw error(404, 'Playlist not found');
	if (!res.ok) throw error(res.status, 'Failed to load playlist');

	const playlist = (await res.json()) as Playlist;

	const width = clamp(url.searchParams.get('maxwidth'), DEFAULT_WIDTH, 200, 1200);
	const height = clamp(url.searchParams.get('maxheight'), DEFAULT_HEIGHT, 160, 1200);
	const embedUrl = `${url.origin}/embed/${encodeURIComponent(parsed.username)}/${encodeURIComponent(parsed.slug)}`;

	setHeaders({ 'cache-control': 'public, max-age=600' });

	return json({
		version: '1.0',
		type: 'rich',
		provider_name: 'Linkbelli',
		provider_url: url.origin,
		title: playlist.name,
		author_name: parsed.username,
		author_url: `${url.origin}/public/${encodeURIComponent(parsed.username)}`,
		width,
		height,
		html:
			`<iframe src="${escapeAttribute(embedUrl)}" width="${width}" height="${height}" ` +
			`frameborder="0" loading="lazy" title="${escapeAttribute(playlist.name)}"></iframe>`
	});
};

/** Accepts a public playlist address on this server, and nothing else. */
function parsePlaylistUrl(target: string, origin: string): { username: string; slug: string } | null {
	let parsed: URL;
	try {
		parsed = new URL(target, origin);
	} catch {
		return null;
	}

	// Only our own addresses: an oEmbed endpoint that describes other people's URLs is a way to
	// make this server fetch and vouch for anything.
	if (parsed.origin !== origin) return null;

	const match = parsed.pathname.match(/^\/(?:public|embed)\/([^/]+)\/([^/]+)\/?$/);
	if (!match) return null;

	return { username: decodeURIComponent(match[1]), slug: decodeURIComponent(match[2]) };
}

function clamp(raw: string | null, fallback: number, min: number, max: number): number {
	const value = Number(raw);
	if (!Number.isFinite(value) || value <= 0) return fallback;
	return Math.min(max, Math.max(min, Math.round(value)));
}

function escapeAttribute(value: string): string {
	return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
