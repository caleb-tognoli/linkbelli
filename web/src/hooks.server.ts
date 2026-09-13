import { redirect, type Handle } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import { ACCESS_COOKIE, REFRESH_COOKIE, clearTokens, setTokens } from '$lib/server/auth';

// Auth pages: redirect already-signed-in users away from these.
const AUTH_PAGES = ['/login', '/register', '/forgot-password', '/reset-password'];

/**
 * Landing pages for a link in an email.
 *
 * Reachable without a session, and — the part that was wrong — not redirected away when there is
 * one. `/unsubscribe` sat in AUTH_PAGES, so somebody signed in who clicked "stop these" in their
 * own digest was bounced to the home page and stayed subscribed. The link works or it does not;
 * whether the person happens to have a session open is not the link's business.
 */
const MAIL_LANDINGS = ['/unsubscribe', '/confirm-email'];

// Anonymous-viewable areas. The /api/v1 proxy is included so anonymous browsers can read public
// endpoints; the API still enforces per-endpoint auth (protected calls get 401).
// "/i" is a shared link: it is opened by whoever it was sent to, who by definition has no
// account here. Bouncing them to a sign-in page would defeat the point of sharing.
// "/invite" is a link sent to somebody who by definition has no account here yet — being asked
// to sign in before being told what the invitation is for is the wrong order.
const ANON_PREFIXES = ['/discover', '/public', '/embed', '/oembed', '/i', '/invite', '/api/v1'];

// Served to crawlers, which never carry a session.
const CRAWLER_FILES = ['/robots.txt', '/sitemap.xml', '/manifest.webmanifest'];

const startsWithSegment = (path: string, prefix: string) =>
	path === prefix || path.startsWith(prefix + '/');

export const handle: Handle = async ({ event, resolve }) => {
	const { cookies, fetch } = event;

	async function refresh(): Promise<boolean> {
		const refreshToken = cookies.get(REFRESH_COOKIE);
		if (!refreshToken) return false;

		const res = await fetch(`${API_BASE}/api/v1/auth/refresh`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ refreshToken })
		});
		if (!res.ok) {
			clearTokens(cookies);
			return false;
		}
		setTokens(cookies, await res.json());
		return true;
	}

	// If the access cookie expired but we still hold a refresh token, mint a fresh one up front.
	if (!cookies.get(ACCESS_COOKIE) && cookies.get(REFRESH_COOKIE)) {
		await refresh();
	}

	// Server-side API caller used by load functions and actions.
	event.locals.api = async (path, init = {}) => {
		const call = () => {
			const access = cookies.get(ACCESS_COOKIE);
			const headers = new Headers(init.headers);
			if (access) headers.set('Authorization', `Bearer ${access}`);
			if (init.body && !headers.has('content-type')) headers.set('content-type', 'application/json');
			return fetch(`${API_BASE}${path}`, { ...init, headers });
		};

		let res = await call();
		if (res.status === 401 && (await refresh())) {
			res = await call();
		}
		return res;
	};

	event.locals.authenticated = Boolean(cookies.get(ACCESS_COOKIE));

	const { pathname } = event.url;
	const isAuthPage = AUTH_PAGES.includes(pathname);
	// "/" is the site introduction — public, so newcomers can read it before signing up.
	// Crawler files have to answer to an anonymous request or they do not work at all.
	const anonAllowed =
		pathname === '/' ||
		CRAWLER_FILES.includes(pathname) ||
		isAuthPage ||
		MAIL_LANDINGS.includes(pathname) ||
		ANON_PREFIXES.some((p) => startsWithSegment(pathname, p));

	if (!event.locals.authenticated && !anonAllowed) {
		const redirectTo = encodeURIComponent(pathname + event.url.search);
		throw redirect(303, `/login?redirectTo=${redirectTo}`);
	}
	if (event.locals.authenticated && isAuthPage) {
		throw redirect(303, '/');
	}

	// Theme preference (light/dark/system) from a cookie, injected into <html data-theme> so the
	// server-rendered markup matches the client (no flash of the wrong theme).
	const theme = cookies.get('lb_theme') ?? 'system';
	const response = await resolve(event, {
		transformPageChunk: ({ html }) => html.replace('__THEME__', theme)
	});

	// Only the embed is meant to be framed. Everything else refuses, which it previously did not:
	// nothing here set a frame policy at all, so any page could be put inside someone's iframe.
	response.headers.set(
		'content-security-policy',
		startsWithSegment(pathname, '/embed') ? 'frame-ancestors *' : "frame-ancestors 'none'"
	);

	// This was the only security header the app sent. The rest are cheap and the app renders a
	// lot of text it did not write — page titles, descriptions, notes, and the full body of
	// scraped articles.
	//
	// No script-src yet: that needs SvelteKit's own csp config to nonce the hydration script,
	// which is a separate change. These are the ones that cost nothing to be right about.
	response.headers.set('x-content-type-options', 'nosniff');
	// Referrers go to whatever a saved link points at. A playlist id in a path is not something
	// to hand to every site somebody opens from here.
	response.headers.set('referrer-policy', 'strict-origin-when-cross-origin');
	response.headers.set('permissions-policy', 'geolocation=(), microphone=(), camera=(), payment=()');

	// Only where the browser reached us over TLS — announcing it over plain http is both ignored
	// and, on a local run, a good way to lock yourself out of your own machine's http origin.
	if (event.url.protocol === 'https:') {
		response.headers.set('strict-transport-security', 'max-age=31536000; includeSubDomains');
	}

	return response;
};
