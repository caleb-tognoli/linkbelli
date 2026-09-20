import { NO_CONNECTION } from './client';

/**
 * What to tell somebody about a write that did not happen.
 *
 * The statuses worth naming are the ones with a different next step: reload for a conflict,
 * wait for a rate limit, sign in again for a lapsed session, wait for a connection when the
 * request never left at all. Everything else gets the caller's own sentence, because a number is
 * not an explanation.
 */
export function failureMessage(status: number, fallback: string): string {
	// The request never left, so nothing on the other end knows about it. See NO_CONNECTION.
	if (status === NO_CONNECTION) return 'Could not reach the server. Try again in a moment.';
	if (status === 409) return 'Somebody changed that first. Reload and try again.';
	if (status === 429) return 'Too many changes at once. Try again in a moment.';
	if (status === 401) return 'You have been signed out. Sign in and try again.';
	if (status === 403) return 'You do not have access to do that any more.';
	return fallback;
}
