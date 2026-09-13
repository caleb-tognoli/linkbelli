import { API_BASE } from '$lib/server/config';
import type { PageServerLoad } from './$types';

/**
 * The landing page for the link in the confirmation mail.
 *
 * The confirmation happens here, on load, rather than behind a button. There is nothing to decide
 * — the person already decided when they clicked the link — and a page that says "click here to
 * confirm" after they clicked a link to confirm is asking the same question twice.
 */
export const load: PageServerLoad = async ({ url, fetch }) => {
	const email = url.searchParams.get('email') ?? '';
	const token = url.searchParams.get('token') ?? '';

	if (!email || !token) {
		return { ok: false, email, error: 'That link is missing something. Ask for a new one.' };
	}

	const res = await fetch(`${API_BASE}/api/v1/auth/confirm-email`, {
		method: 'POST',
		headers: { 'content-type': 'application/json' },
		body: JSON.stringify({ email, token })
	});

	if (res.ok) {
		return { ok: true, email, error: null };
	}

	if (res.status === 429) {
		return { ok: false, email, error: 'Too many attempts. Wait a minute and open the link again.' };
	}

	// The API's own message is the useful one: it distinguishes a truncated link from an expired
	// one, and both from a link that was never ours.
	const body = await res.json().catch(() => null);
	const detail = body?.errors?.token?.[0] ?? body?.detail;

	return { ok: false, email, error: detail ?? 'That link did not work. Ask for a new one.' };
};
