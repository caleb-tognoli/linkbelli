import type { RequestHandler } from './$types';

/**
 * What one request to the API brings back, and the most URLs one sitemap may hold.
 *
 * 5 000 is the API's own page size for this endpoint, chosen because a row is three short fields.
 * 50 000 is the sitemaps protocol's limit, not ours — past it a crawler is entitled to ignore the
 * file, so stopping there and saying so beats silently handing over an invalid one.
 */
const PAGE_SIZE = 5000;
const MAX_URLS = 50_000;

/** How long a rendered sitemap is reused. Crawlers ask often, and from many addresses. */
const CACHE_SECONDS = 3600;

interface SitemapEntry {
	ownerUsername: string;
	slug: string;
	lastModified: string;
}

/**
 * The last render, kept so a crawl does not cost a database pass per fetch.
 *
 * `cache-control: public, max-age=3600` only helps whoever is caching in front; the origin still
 * did the whole job on every miss, and crawlers come from many IPs at once. This is per web
 * container and per origin — the origin is in every URL in the body, so one process serving two
 * hostnames must not hand the second one the first one's file.
 *
 * Bounded, because the origin can be derived from a request header: an unbounded map keyed on
 * something a caller chooses is a way to fill a container's memory one `Host` at a time. A real
 * deployment has one origin, or two.
 */
const MAX_CACHED_ORIGINS = 4;
const rendered = new Map<string, { at: number; body: string }>();

function remember(origin: string, body: string) {
	// Oldest inserted first, so deleting the first key evicts least-recently-rendered.
	if (!rendered.has(origin) && rendered.size >= MAX_CACHED_ORIGINS) {
		const oldest = rendered.keys().next();
		if (!oldest.done) rendered.delete(oldest.value);
	}

	rendered.set(origin, { at: Date.now(), body });
}

/**
 * Lists the public playlists so they can be indexed. Only `Public` is included — the API's
 * sitemap endpoint never returns `Unlisted`, which is share-by-link and deliberately unfindable.
 */
export const GET: RequestHandler = async ({ locals, url, setHeaders }) => {
	setHeaders({ 'cache-control': `public, max-age=${CACHE_SECONDS}` });

	const cached = rendered.get(url.origin);
	if (cached && Date.now() - cached.at < CACHE_SECONDS * 1000) {
		return xml(cached.body);
	}

	const entries: string[] = [
		urlEntry(`${url.origin}/`, undefined, 'daily'),
		urlEntry(`${url.origin}/discover`, undefined, 'daily')
	];

	const profiles = new Set<string>();
	let truncated = false;

	let cursor: string | null = null;
	// Almost always one pass: an instance with fewer than five thousand public playlists — which
	// is every instance — is a single request.
	while (entries.length + profiles.size < MAX_URLS) {
		const qs = new URLSearchParams({ limit: String(PAGE_SIZE) });
		if (cursor) qs.set('cursor', cursor);

		const res = await locals.api(`/api/v1/public/sitemap?${qs}`);
		if (!res.ok) break;

		const body = (await res.json()) as { items: SitemapEntry[]; nextCursor: string | null };
		for (const playlist of body.items) {
			// Accounts made before usernames were validated can hold anything, including an email
			// address — people typed one in because the sign-in field accepts either. Their
			// playlists still work and are still reachable; what stops here is handing the name
			// to crawlers. See UsernamePolicy on the API side, which is the same rule.
			if (!isPublishableUsername(playlist.ownerUsername)) continue;

			entries.push(
				urlEntry(
					`${url.origin}/public/${encodeURIComponent(playlist.ownerUsername)}/${encodeURIComponent(playlist.slug)}`,
					playlist.lastModified,
					'weekly'
				)
			);
			// One profile entry per owner, however many playlists they have published.
			profiles.add(playlist.ownerUsername);
		}

		cursor = body.nextCursor;
		if (!cursor) break;

		// There is more, and no room for it. Said out loud: this used to stop at five thousand
		// with nothing anywhere recording that the rest of the site had gone unlisted.
		if (entries.length + profiles.size >= MAX_URLS) truncated = true;
	}

	for (const username of profiles) {
		entries.push(urlEntry(`${url.origin}/public/${encodeURIComponent(username)}`, undefined, 'weekly'));
	}

	if (truncated) {
		console.warn(
			`Linkbelli: the sitemap reached the ${MAX_URLS.toLocaleString()}-URL limit of the ` +
				`sitemaps protocol and stopped. Playlists beyond that are not listed for crawlers.`
		);
	}

	const body = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${entries.join('\n')}
</urlset>
`;

	remember(url.origin, body);
	return xml(body);
};

function xml(body: string): Response {
	return new Response(body, { headers: { 'content-type': 'application/xml; charset=utf-8' } });
}

/**
 * Mirrors the API's UsernamePolicy: letters, digits, hyphen and underscore, 3–30 characters.
 * Duplicated rather than fetched because it guards a crawler-facing file and should not depend
 * on a round trip to decide whether to leak somebody's address.
 */
function isPublishableUsername(username: string): boolean {
	return /^[A-Za-z0-9][A-Za-z0-9_-]{1,28}[A-Za-z0-9]$/.test(username);
}

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
