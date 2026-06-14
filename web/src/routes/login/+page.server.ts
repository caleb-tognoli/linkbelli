import { fail, redirect } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import { setTokens } from '$lib/server/auth';
import type { Actions } from './$types';

export const actions: Actions = {
	default: async ({ request, cookies, fetch, url }) => {
		const data = await request.formData();
		const login = String(data.get('login') ?? '').trim();
		const password = String(data.get('password') ?? '');

		if (!login || !password) {
			return fail(400, { error: 'Enter your username/email and password.', login });
		}

		const res = await fetch(`${API_BASE}/api/v1/auth/login`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ login, password })
		});

		if (!res.ok) {
			return fail(401, { error: 'Invalid credentials.', login });
		}

		setTokens(cookies, await res.json());

		const redirectTo = url.searchParams.get('redirectTo');
		// Only allow same-origin paths. Reject protocol-relative ("//evil.com") and
		// backslash-prefixed ("/\evil.com") values, which browsers resolve as absolute URLs.
		const safeRedirect =
			redirectTo && redirectTo.startsWith('/') && !redirectTo.startsWith('//') && !redirectTo.startsWith('/\\')
				? redirectTo
				: '/';
		throw redirect(303, safeRedirect);
	}
};
