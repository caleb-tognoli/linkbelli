import type { RequestHandler } from './$types';

/**
 * Only the public surface is crawlable. Everything behind auth is off limits — those pages
 * redirect to /login for a crawler anyway, and listing them just invites the attempt. Unlisted
 * playlists live under /public/ but carry their own noindex, since they are share-by-link
 * rather than published.
 */
export const GET: RequestHandler = ({ url }) => {
	const body = `User-agent: *
Allow: /$
Allow: /discover
Allow: /public/
Disallow: /playlists
Disallow: /folders
Disallow: /sources
Disallow: /import
Disallow: /profile
Disallow: /settings
Disallow: /trash
Disallow: /login
Disallow: /register
Disallow: /api/

Sitemap: ${url.origin}/sitemap.xml
`;

	return new Response(body, {
		headers: {
			'content-type': 'text/plain; charset=utf-8',
			'cache-control': 'public, max-age=3600'
		}
	});
};
