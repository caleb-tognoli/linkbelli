import { redirect, type Handle } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import { ACCESS_COOKIE, REFRESH_COOKIE, clearTokens, setTokens } from '$lib/server/auth';

// Auth pages: redirect already-signed-in users away from these.
const AUTH_PAGES = [
	'/login',
	'/register',
	'/forgot-password',
	'/reset-password',
	// Reached from an email, by somebody who may not be signed in and should not have to be.
	'/unsubscribe'
];

// Anonymous-viewable areas. The /api/v1 proxy is included so anonymous browsers can read public
// endpoints; the API still enforces per-endpoint auth (protected calls get 401).
// "/i" is a shared link: it is opened by whoever it was sent to, who by definition has no
// account here. Bouncing them to a sign-in page would defeat the point of sharing.
const ANON_PREFIXES = ['/discover', '/public', '/embed', '/oembed', '/i', '/api/v1'];

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

	return response;
};
