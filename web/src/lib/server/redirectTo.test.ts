import { describe, expect, it } from 'vitest';
import { safeRedirect } from './redirectTo';

/**
 * Where somebody lands after signing in or signing up.
 *
 * This guard used to live inline in the login action only, so registering ignored `redirectTo`
 * entirely — which meant somebody making an account from an invitation link landed on the home
 * page holding a link they had half used. Shared, so both ends behave the same and both are
 * covered by these.
 */
describe('safeRedirect', () => {
	it('keeps an ordinary path', () => {
		expect(safeRedirect('/invite/abc123')).toBe('/invite/abc123');
		expect(safeRedirect('/playlists?tag=rust')).toBe('/playlists?tag=rust');
	});

	it('falls back when there is nothing to go on', () => {
		expect(safeRedirect(null)).toBe('/');
		expect(safeRedirect('')).toBe('/');
	});

	/**
	 * The whole reason the guard exists: a sign-in page that forwards anywhere is an open
	 * redirect. Both of the first two are resolved by browsers as absolute URLs despite starting
	 * with a slash, which is what makes them worth naming rather than assuming.
	 */
	it('refuses anything that leaves this origin', () => {
		expect(safeRedirect('//evil.example')).toBe('/');
		expect(safeRedirect('/\\evil.example')).toBe('/');
		expect(safeRedirect('https://evil.example')).toBe('/');
		expect(safeRedirect('evil.example')).toBe('/');
	});

	it('takes the fallback it is given', () => {
		expect(safeRedirect(null, '/queue')).toBe('/queue');
	});
});
