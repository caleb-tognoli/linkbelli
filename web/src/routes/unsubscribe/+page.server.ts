import { API_BASE } from '$lib/server/config';
import type { PageServerLoad } from './$types';

/**
 * Acted on during load, not behind a button.
 *
 * Somebody clicking "stop these" in an email has already decided. Making them arrive at a page
 * and press a second button is a way to lose the ones who close the tab — and those people do not
 * go and find the settings page, they mark the message as spam.
 */
export const load: PageServerLoad = async ({ url, fetch }) => {
	const token = url.searchParams.get('token');
	if (!token) {
		return { ok: false, description: null };
	}

	const res = await fetch(`${API_BASE}/api/v1/notifications/unsubscribe`, {
		method: 'POST',
		headers: { 'content-type': 'application/json' },
		body: JSON.stringify({ token })
	});

	if (!res.ok) {
		return { ok: false, description: null };
	}

	const body = (await res.json()) as { description?: string };

	return { ok: true, description: body.description ?? null };
};
