import { redirect, type Handle } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import { ACCESS_COOKIE, REFRESH_COOKIE, clearTokens, setTokens } from '$lib/server/auth';

// Auth pages: redirect already-signed-in users away from these.
const AUTH_PAGES = ['/login', '/register'];

// Anonymous-viewable areas. The /api/v1 proxy is included so anonymous browsers can read public
// endpoints; the API still enforces per-endpoint auth (protected calls get 401).
const ANON_PREFIXES = ['/discover', '/public', '/api/v1'];

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
	const anonAllowed = isAuthPage || ANON_PREFIXES.some((p) => startsWithSegment(pathname, p));

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
	return resolve(event, {
		transformPageChunk: ({ html }) => html.replace('__THEME__', theme)
	});
};
