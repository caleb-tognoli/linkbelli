/// <reference types="@sveltejs/kit" />
/// <reference lib="webworker" />

import { build, files, version } from '$service-worker';

/**
 * What this is for, and deliberately not for.
 *
 * The app is a view onto data that lives on a server, and showing somebody a stale playlist as
 * though it were current would be worse than telling them the truth. So nothing from the API is
 * ever cached. What is cached is the shell — the code and the assets — so that opening the app
 * with no connection gets the app rather than the browser's error page, and a link shared from
 * the system share sheet reaches a form that can queue it.
 */
const self = globalThis.self as unknown as ServiceWorkerGlobalScope;

const CACHE = `linkbelli-shell-${version}`;

/** The built bundle and everything in static/ — fingerprinted, so safe to cache outright. */
const PRECACHE = [...build, ...files];

/**
 * Pages worth keeping a copy of.
 *
 * `/save` earns its place: it is where the share sheet lands, and it is the one screen whose
 * whole job can still be done with no connection. The others are the ones somebody opening a
 * cold app would expect to see something on.
 */
const SHELL_ROUTES = ['/save', '/playlists', '/queue'];

self.addEventListener('install', (event) => {
	event.waitUntil(
		(async () => {
			const cache = await caches.open(CACHE);
			await cache.addAll(PRECACHE);

			// Best effort, one at a time: a shell page that cannot be fetched at install time is
			// not a reason to leave the whole worker uninstalled.
			await Promise.all(
				SHELL_ROUTES.map(async (route) => {
					try {
						const res = await fetch(route);
						if (res.ok) await cache.put(route, res);
					} catch {
						// Installed offline, or the route needs a session. Either way, later visits
						// will fill this in.
					}
				})
			);

			await self.skipWaiting();
		})()
	);
});

self.addEventListener('activate', (event) => {
	event.waitUntil(
		(async () => {
			// Every version gets its own cache, so old bundles go when their version does.
			for (const key of await caches.keys()) {
				if (key.startsWith('linkbelli-shell-') && key !== CACHE) await caches.delete(key);
			}

			await self.clients.claim();
		})()
	);
});

self.addEventListener('fetch', (event) => {
	const request = event.request;
	if (request.method !== 'GET') return;

	const url = new URL(request.url);
	if (url.origin !== self.location.origin) return;

	// Never the API. A cached list of links is a lie with a timestamp on it, and a cached write
	// is worse; the offline queue is what handles writes that cannot get through.
	if (url.pathname.startsWith('/api/')) return;

	// Fingerprinted assets: the name changes when the content does, so the cache cannot be stale.
	if (PRECACHE.includes(url.pathname)) {
		event.respondWith(cacheFirst(request));
		return;
	}

	if (request.mode === 'navigate') {
		event.respondWith(navigateWithFallback(request, url));
	}
});

async function cacheFirst(request: Request): Promise<Response> {
	const cache = await caches.open(CACHE);
	const hit = await cache.match(request);
	if (hit) return hit;

	const res = await fetch(request);
	if (res.ok) cache.put(request, res.clone());

	return res;
}

/**
 * Network first, because the page is rendered from live data and the live one is the right one.
 * The cache is only what stands in when there is no network at all.
 */
async function navigateWithFallback(request: Request, url: URL): Promise<Response> {
	const cache = await caches.open(CACHE);

	try {
		const res = await fetch(request);

		// Kept under the path alone, without the query string: the share sheet appends a
		// different URL every time, so caching per full address would never once hit.
		if (res.ok && SHELL_ROUTES.includes(url.pathname)) {
			cache.put(url.pathname, res.clone());
		}

		return res;
	} catch {
		const hit = (await cache.match(url.pathname)) ?? (await cache.match('/save'));
		if (hit) return hit;

		return offlineResponse();
	}
}

/**
 * The last resort, when even the shell was never cached.
 *
 * Written out here rather than fetched from a route, because the one thing that is certainly
 * unavailable in this branch is the server.
 */
function offlineResponse(): Response {
	return new Response(
		`<!doctype html>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Offline - linkbelli</title>
<style>
  :root { color-scheme: light dark; font-family: system-ui, sans-serif }
  body { margin: 0; display: grid; place-items: center; min-height: 100vh; padding: 1.5rem }
  main { max-width: 24rem; text-align: center }
  h1 { font-size: 1.25rem; margin: 0 0 .5rem }
  p { margin: 0; opacity: .7; line-height: 1.5 }
</style>
<main>
  <h1>No connection</h1>
  <p>Linkbelli needs the network for this page. Anything you saved while offline is still
  waiting and will be sent when you are back.</p>
</main>`,
		{ status: 503, headers: { 'content-type': 'text/html; charset=utf-8' } }
	);
}
