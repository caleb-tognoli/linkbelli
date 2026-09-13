/** A backslash-prefixed path, which browsers resolve as an absolute URL. */
const BACKSLASH_PREFIX = '/\\';

/**
 * Where to send somebody after they sign in or sign up.
 *
 * Same-origin paths only. A protocol-relative `//evil.com` and a backslash-prefixed one are both
 * resolved as absolute URLs by browsers, which is how a login page becomes an open redirect — so
 * both are refused, along with anything that is not a path at all.
 */
export function safeRedirect(value: string | null, fallback = '/'): string {
	if (
		!value ||
		!value.startsWith('/') ||
		value.startsWith('//') ||
		value.startsWith(BACKSLASH_PREFIX)
	) {
		return fallback;
	}

	return value;
}
