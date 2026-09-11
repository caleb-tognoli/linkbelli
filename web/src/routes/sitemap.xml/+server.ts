import type { PublicPlaylistSummary } from '$lib/types';
import type { RequestHandler } from './$types';

/** Pages per request from the discovery endpoint, and how many pages to walk at most. */
const PAGE_SIZE = 100;
const MAX_PAGES = 50;

/**
 * Lists the public playlists so they can be indexed. Only `Public` is included — discovery never
 * returns `Unlisted`, which is share-by-link and deliberately unfindable.
 */
export const GET: RequestHandler = async ({ locals, url, setHeaders }) => {
	const entries: string[] = [
		urlEntry(`${url.origin}/`, undefined, 'daily'),
		urlEntry(`${url.origin}/discover`, undefined, 'daily')
	];

	const profiles = new Set<string>();

	let cursor: string | null = null;
	for (let page = 0; page < MAX_PAGES; page++) {
		const qs = new URLSearchParams({ limit: String(PAGE_SIZE) });
		if (cursor) qs.set('cursor', cursor);

		const res = await locals.api(`/api/v1/public/playlists?${qs}`);
		if (!res.ok) break;

		const body = (await res.json()) as { items: PublicPlaylistSummary[]; nextCursor: string | null };
		for (const playlist of body.items) {
			entries.push(
				urlEntry(
					`${url.origin}/public/${encodeURIComponent(playlist.ownerUsername)}/${encodeURIComponent(playlist.slug)}`,
					playlist.creationTime,
					'weekly'
				)
			);
			// One profile entry per owner, however many playlists they have published.
			profiles.add(playlist.ownerUsername);
		}

		cursor = body.nextCursor;
		if (!cursor) break;
	}

	for (const username of profiles) {
		entries.push(urlEntry(`${url.origin}/public/${encodeURIComponent(username)}`, undefined, 'weekly'));
	}

	setHeaders({ 'cache-control': 'public, max-age=3600' });

	return new Response(
		`<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${entries.join('\n')}
</urlset>
`,
		{ headers: { 'content-type': 'application/xml; charset=utf-8' } }
	);
};

function urlEntry(loc: string, lastmod: string | undefined, changefreq: string): string {
	const modified = lastmod ? `\n    <lastmod>${lastmod.slice(0, 10)}</lastmod>` : '';
	return `  <url>
    <loc>${escapeXml(loc)}</loc>${modified}
    <changefreq>${changefreq}</changefreq>
  </url>`;
}

function escapeXml(value: string): string {
	return value
		.replace(/&/g, '&amp;')
		.replace(/</g, '&lt;')
		.replace(/>/g, '&gt;')
		.replace(/"/g, '&quot;')
		.replace(/'/g, '&apos;');
}
