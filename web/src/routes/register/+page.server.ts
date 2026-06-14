import { fail, redirect } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import { setTokens } from '$lib/server/auth';
import type { Actions } from './$types';

/** Pull a readable message out of an RFC7807 ValidationProblemDetails body. */
function firstValidationError(body: unknown): string | null {
	if (body && typeof body === 'object' && 'errors' in body) {
		const errors = (body as { errors: Record<string, string[]> }).errors;
		for (const messages of Object.values(errors)) {
			if (messages?.length) return messages[0];
		}
	}
	return null;
}

export const actions: Actions = {
	default: async ({ request, cookies, fetch }) => {
		const data = await request.formData();
		const username = String(data.get('username') ?? '').trim();
		const email = String(data.get('email') ?? '').trim();
		const password = String(data.get('password') ?? '');
		const fields = { username, email };

		if (!username || !email || !password) {
			return fail(400, { error: 'All fields are required.', ...fields });
		}

		const register = await fetch(`${API_BASE}/api/v1/auth/register`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ username, email, password })
		});

		if (!register.ok) {
			const body = await register.json().catch(() => null);
			return fail(400, {
				error: firstValidationError(body) ?? 'Could not create the account.',
				...fields
			});
		}

		// Auto sign-in so the user lands straight in the app.
		const login = await fetch(`${API_BASE}/api/v1/auth/login`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ login: username, password })
		});

		if (!login.ok) {
			// Account exists but auto-login failed — send them to the login page.
			throw redirect(303, '/login');
		}

		setTokens(cookies, await login.json());
		throw redirect(303, '/');
	}
};
