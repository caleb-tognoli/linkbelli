import { error } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

const FORMATS = new Set(['rss', 'atom', 'json', 'xml']);

/**
 * A readable address for a playlist's feed. The API is the one that builds it; this exists so
 * people can subscribe to `/public/alice/weekend-reading/feed.rss` rather than to a path with
 * `/api/v1/` in the middle of it.
 */
export const GET: RequestHandler = async ({ locals, params, setHeaders }) => {
	if (!FORMATS.has(params.ext)) throw error(404, 'Not found');

	const upstream = await locals.api(
		`/api/v1/public/playlists/${encodeURIComponent(params.username)}/${encodeURIComponent(params.slug)}/feed.${params.ext}`
	);

	if (upstream.status === 404) throw error(404, 'Playlist not found');
	if (!upstream.ok) throw error(502, 'Could not build the feed');

	// Readers poll; a few minutes of caching spares the database without making the feed stale.
	setHeaders({ 'cache-control': 'public, max-age=300' });

	return new Response(await upstream.text(), {
		headers: {
			'content-type': upstream.headers.get('content-type') ?? 'application/xml; charset=utf-8'
		}
	});
};
