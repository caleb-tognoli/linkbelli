import { fail } from '@sveltejs/kit';
import { API_BASE } from '$lib/server/config';
import type { Actions } from './$types';

export const actions: Actions = {
	default: async ({ request, fetch }) => {
		const data = await request.formData();
		const login = String(data.get('login') ?? '').trim();

		if (!login) {
			return fail(400, { error: 'Enter your username or email.', login });
		}

		const res = await fetch(`${API_BASE}/api/v1/auth/forgot-password`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ login })
		});

		// The one case worth distinguishing: this deployment cannot send mail at all, so nobody
		// is going to receive anything and saying "check your inbox" would be a lie.
		if (res.status === 503) {
			return fail(503, {
				error: 'This Linkbelli has no mail configured, so it cannot send a reset link.',
				login
			});
		}

		if (res.status === 429) {
			return fail(429, { error: 'Too many attempts. Wait a minute and try again.', login });
		}

		// Everything else reports the same success, including an account that does not exist.
		// Anything else would turn this form into a way to find out who has one.
		return { sent: true };
	}
};
