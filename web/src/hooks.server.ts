import { redirect, type Handle } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import { ACCESS_COOKIE, REFRESH_COOKIE, clearTokens, setTokens } from '$lib/server/auth';

// Routes reachable without authentication. Everything else requires a session.
const PUBLIC_PATHS = ['/login', '/register'];

function isPublic(pathname: string): boolean {
	return PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(p + '/'));
}

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
	if (!event.locals.authenticated && !isPublic(pathname)) {
		const redirectTo = encodeURIComponent(pathname + event.url.search);
		throw redirect(303, `/login?redirectTo=${redirectTo}`);
	}
	if (event.locals.authenticated && isPublic(pathname)) {
		throw redirect(303, '/');
	}

	return resolve(event);
};
