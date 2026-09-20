import { NO_CONNECTION } from './client';

/** How long the server asked us to wait, in seconds, or null when it did not say. */
function retryAfterSeconds(res: Response): number | null {
	const header = res.headers.get('retry-after');
	if (!header) return null;

	const seconds = Number(header);
	if (Number.isFinite(seconds) && seconds > 0) return Math.round(seconds);

	// The header may also be a date. Anything else is not worth guessing at.
	const at = Date.parse(header);
	if (Number.isNaN(at)) return null;

	const wait = Math.round((at - Date.now()) / 1000);
	return wait > 0 ? wait : null;
}

/** "in about a minute", "in about 20 seconds" — a wait somebody can act on. */
function inAbout(seconds: number): string {
	if (seconds < 45) return `in about ${seconds} seconds`;

	const minutes = Math.round(seconds / 60);
	return minutes <= 1 ? 'in about a minute' : `in about ${minutes} minutes`;
}

/**
 * What to tell somebody about a write that did not happen.
 *
 * The statuses worth naming are the ones with a different next step: reload for a conflict,
 * wait for a rate limit, sign in again for a lapsed session, wait for a connection when the
 * request never left at all. Everything else gets the caller's own sentence, because a number is
 * not an explanation.
 *
 * Takes the response rather than its status where it can, so a rate limit can say how long —
 * the API sends a Retry-After on every 429 and the proxy forwards it, and "in a moment" was the
 * app throwing that away and guessing.
 */
export function failureMessage(from: number | Response, fallback: string): string {
	const status = typeof from === 'number' ? from : from.status;
	const res = typeof from === 'number' ? null : from;

	// The request never left, so nothing on the other end knows about it. See NO_CONNECTION.
	if (status === NO_CONNECTION) return 'Could not reach the server. Try again in a moment.';
	if (status === 409) return 'Somebody changed that first. Reload and try again.';
	if (status === 429) {
		const wait = res && retryAfterSeconds(res);
		return wait
			? `Too many changes at once. Try again ${inAbout(wait)}.`
			: 'Too many changes at once. Try again in a moment.';
	}
	if (status === 401) return 'You have been signed out. Sign in and try again.';
	if (status === 403) return 'You do not have access to do that any more.';
	return fallback;
}
