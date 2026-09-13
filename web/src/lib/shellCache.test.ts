import { describe, expect, it } from 'vitest';
import { shellCacheBlocked } from './shellCache';

/**
 * Whether the app will keep a copy of itself.
 *
 * This was previously attempted and its failure discarded, so a browser that refused looked
 * exactly like one that succeeded. These cover the half that can be decided before asking the
 * browser at all — the half that decides whether the notice is even reachable.
 */
describe('shellCacheBlocked', () => {
	it('gives up when the browser has no service workers', () => {
		const blocked = shellCacheBlocked({
			hasServiceWorker: false,
			protocol: 'https:',
			hostname: 'linkbelli.example'
		});

		expect(blocked?.status).toBe('unsupported');
	});

	it('gives up over plain http, and says why', () => {
		const blocked = shellCacheBlocked({
			hasServiceWorker: true,
			protocol: 'http:',
			hostname: 'linkbelli.example'
		});

		expect(blocked?.status).toBe('unsupported');
		// Worth a sentence: "unsupported" on an https-capable browser is otherwise baffling.
		expect(blocked?.reason).toMatch(/secure/i);
	});

	it('allows an attempt over https', () => {
		expect(
			shellCacheBlocked({
				hasServiceWorker: true,
				protocol: 'https:',
				hostname: 'linkbelli.example'
			})
		).toBeNull();
	});

	/**
	 * The exemption that keeps development honest. Browsers treat localhost as a secure origin,
	 * and without this every local run would report the feature unavailable — which teaches the
	 * maintainer to ignore the notice, defeating the point of having one.
	 */
	it('allows an attempt on localhost over plain http', () => {
		expect(
			shellCacheBlocked({ hasServiceWorker: true, protocol: 'http:', hostname: 'localhost' })
		).toBeNull();
	});
});
