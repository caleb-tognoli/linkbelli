import { vi } from 'vitest';

/** One answer the fake API gives: a response, or a thrown error for "no connection". */
export type Answer = Response | Error | (() => Response | Error | Promise<Response>);

/**
 * Stands in for the web server's /api/v1 proxy in component tests.
 *
 * Routes are "METHOD /path" (path without the /api/v1 prefix, query string ignored). Anything
 * unrouted is a 404, so a component calling something the test did not expect fails loudly
 * rather than quietly getting an empty answer. Every call is recorded with its parsed body.
 */
export function fakeApi(routes: Record<string, Answer>) {
	const calls: { method: string; path: string; body: unknown }[] = [];

	const fetch = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
		const method = (init.method ?? 'GET').toUpperCase();
		const path = String(input).replace(/^\/api\/v1/, '').split('?')[0];
		const body = typeof init.body === 'string' ? JSON.parse(init.body) : undefined;
		calls.push({ method, path, body });

		const answer = routes[`${method} ${path}`];
		const resolved = typeof answer === 'function' ? await answer() : answer;

		if (resolved instanceof Error) throw resolved;
		return resolved ?? new Response('not routed in this test', { status: 404 });
	});

	vi.stubGlobal('fetch', fetch);
	return { fetch, calls };
}

export function json(body: unknown, status = 200): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}
