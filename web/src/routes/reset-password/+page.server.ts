import { fail, redirect } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import type { Actions, PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ url }) => {
	// Straight off the link. Kept in the page rather than a cookie so opening the mail on a
	// different device than the one that asked still works.
	return {
		email: url.searchParams.get('email') ?? '',
		token: url.searchParams.get('token') ?? ''
	};
};

export const actions: Actions = {
	default: async ({ request, fetch }) => {
		const data = await request.formData();
		const email = String(data.get('email') ?? '').trim();
		const token = String(data.get('token') ?? '');
		const password = String(data.get('password') ?? '');
		const confirm = String(data.get('confirm') ?? '');

		if (!email || !token) {
			return fail(400, {
				error: 'That link is missing something. Ask for a new one.'
			});
		}

		if (!password) {
			return fail(400, { error: 'Choose a password.' });
		}

		// Checked here rather than only in the browser, and before the request: a typo in a
		// password nobody can see is the most likely thing to go wrong on this form, and burning
		// the one-use token on it would make the person start over.
		if (password !== confirm) {
			return fail(400, { error: 'Those two passwords are not the same.' });
		}

		const res = await fetch(`${API_BASE}/api/v1/auth/reset-password`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ email, token, newPassword: password })
		});

		if (res.status === 429) {
			return fail(429, { error: 'Too many attempts. Wait a minute and try again.' });
		}

		if (!res.ok) {
			// The API's own messages are the useful ones here — a password policy failure says
			// what is missing, and an expired link says to ask again.
			const body = await res.json().catch(() => null);
			const detail = body?.errors?.password?.[0] ?? body?.detail;

			return fail(res.status, {
				error: detail ?? 'That did not work. Ask for a new link.'
			});
		}

		// Not signed in automatically: every session was just invalidated, which is the point of
		// a reset, and signing in with the new password is how somebody confirms it took.
		throw redirect(303, '/login?reset=1');
	}
};
