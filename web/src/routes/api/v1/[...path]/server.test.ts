import { describe, expect, it, vi } from 'vitest';
import { UpstreamTimeout } from '$lib/server/upstream';
import { DELETE, GET, POST } from './+server';

/**
 * The proxy every browser request to the API passes through.
 *
 * It was the thinnest and least-tested file in the app, and it dropped every response header but
 * one — which silently disabled conditional GETs, download filenames and thumbnail caching, all
 * of which the API implements and none of which anything could use. These assertions are mostly
 * "the header the API sent came out the other side", because that is the whole job.
 */

const ORIGIN = 'http://localhost:5173';

/** A stand-in for locals.api that records what it was called with and replies how it is told. */
function upstream(reply: Response) {
	const calls: { path: string; init: RequestInit }[] = [];
	const api = vi.fn(async (path: string, init: RequestInit = {}) => {
		calls.push({ path, init });
		return reply;
	});
	return { api, calls };
}

function event(opts: {
	method?: string;
	path?: string;
	search?: string;
	headers?: Record<string, string>;
	body?: BodyInit;
	// eslint-disable-next-line @typescript-eslint/no-explicit-any
	api: any;
	clientAddress?: string;
}) {
	const method = opts.method ?? 'GET';
	const url = new URL(`${ORIGIN}/api/v1/${opts.path ?? 'playlists'}${opts.search ?? ''}`);
	return {
		params: { path: opts.path ?? 'playlists' },
		url,
		request: new Request(url, { method, headers: opts.headers, body: opts.body }),
		locals: { api: opts.api, authenticated: true, requestId: 'trace-abc' },
		getClientAddress: () => opts.clientAddress ?? '203.0.113.7'
		// eslint-disable-next-line @typescript-eslint/no-explicit-any
	} as any;
}

describe('BFF proxy — response headers', () => {
	it('passes through the headers the API actually uses', async () => {
		const { api } = upstream(
			new Response('{}', {
				headers: {
					'content-type': 'application/json',
					etag: 'W/"abc"',
					'cache-control': 'public, max-age=86400',
					'content-disposition': 'attachment; filename=linkbelli-2026-01-01.csv',
					'retry-after': '60',
					'last-modified': 'Wed, 01 Jan 2026 00:00:00 GMT'
				}
			})
		);

		const res = await GET(event({ api }));

		expect(res.headers.get('etag')).toBe('W/"abc"');
		expect(res.headers.get('cache-control')).toBe('public, max-age=86400');
		expect(res.headers.get('content-disposition')).toContain('linkbelli-2026-01-01.csv');
		expect(res.headers.get('retry-after')).toBe('60');
		expect(res.headers.get('last-modified')).toBe('Wed, 01 Jan 2026 00:00:00 GMT');
	});

	it('does not pass a Set-Cookie from the API down to the browser', async () => {
		const { api } = upstream(
			new Response('{}', { headers: { 'set-cookie': 'evil=1', 'content-type': 'application/json' } })
		);

		const res = await GET(event({ api }));

		expect(res.headers.get('set-cookie')).toBeNull();
	});

	it('defaults the content type when the API sends none', async () => {
		// Response() supplies one of its own, so it has to be removed to model an API reply
		// that genuinely carries none.
		const bare = new Response('{}');
		bare.headers.delete('content-type');
		const { api } = upstream(bare);

		const res = await GET(event({ api }));

		expect(res.headers.get('content-type')).toBe('application/json');
	});
});

describe('BFF proxy — request headers', () => {
	it('forwards the conditional-GET headers, so an ETag can produce a 304', async () => {
		const { api, calls } = upstream(new Response(null, { status: 304 }));

		await GET(event({ api, headers: { 'if-none-match': 'W/"abc"' } }));

		expect(new Headers(calls[0].init.headers).get('if-none-match')).toBe('W/"abc"');
	});

	it('forwards Idempotency-Key, so a retried POST can be made safe', async () => {
		const { api, calls } = upstream(new Response('{}', { status: 201 }));

		await POST(
			event({
				method: 'POST',
				api,
				headers: { origin: ORIGIN, 'content-type': 'application/json', 'idempotency-key': 'k-1' },
				body: '{"url":"https://example.com"}'
			})
		);

		expect(new Headers(calls[0].init.headers).get('idempotency-key')).toBe('k-1');
	});

	it('never forwards the browser cookie or an Authorization the page set', async () => {
		const { api, calls } = upstream(new Response('{}'));

		await GET(event({ api, headers: { cookie: 'lb_access=leak', authorization: 'Bearer forged' } }));

		const sent = new Headers(calls[0].init.headers);
		expect(sent.get('cookie')).toBeNull();
		expect(sent.get('authorization')).toBeNull();
	});

	it('tells the API who is calling, so anonymous rate limits are per visitor', async () => {
		const { api, calls } = upstream(new Response('{}'));

		await GET(event({ api, clientAddress: '198.51.100.4' }));

		expect(new Headers(calls[0].init.headers).get('x-forwarded-for')).toBe('198.51.100.4');
	});

	it('appends to an existing forwarded-for chain rather than replacing it', async () => {
		const { api, calls } = upstream(new Response('{}'));

		await GET(event({ api, headers: { 'x-forwarded-for': '203.0.113.1' }, clientAddress: '10.0.0.5' }));

		expect(new Headers(calls[0].init.headers).get('x-forwarded-for')).toBe('203.0.113.1, 10.0.0.5');
	});
});

describe('BFF proxy — bodies and methods', () => {
	it('sends the body as bytes, so a non-UTF-8 payload is not mangled', async () => {
		const { api, calls } = upstream(new Response('{}', { status: 201 }));
		const bytes = new Uint8Array([0xff, 0xfe, 0x00, 0x41]);

		await POST(
			event({
				method: 'POST',
				api,
				headers: { origin: ORIGIN, 'content-type': 'application/octet-stream' },
				body: bytes
			})
		);

		expect(new Uint8Array(calls[0].init.body as ArrayBuffer)).toEqual(bytes);
	});

	it('sends no body at all for a DELETE that had none', async () => {
		const { api, calls } = upstream(new Response(null, { status: 204 }));

		await DELETE(event({ method: 'DELETE', api, headers: { origin: ORIGIN } }));

		expect(calls[0].init.body).toBeUndefined();
	});

	it.each([204, 304])('returns no body on a %i', async (status) => {
		const { api } = upstream(new Response(null, { status }));
		const res = await GET(event({ api }));
		expect(res.status).toBe(status);
		expect(res.body).toBeNull();
	});

	it('carries the query string upstream', async () => {
		const { api, calls } = upstream(new Response('{}'));
		await GET(event({ api, path: 'search', search: '?q=rust&limit=10' }));
		expect(calls[0].path).toBe('/api/v1/search?q=rust&limit=10');
	});
});

describe('BFF proxy — CSRF', () => {
	it('refuses an unsafe method from another origin', async () => {
		const { api, calls } = upstream(new Response('{}'));

		await expect(
			POST(event({ method: 'POST', api, headers: { origin: 'https://evil.example' }, body: '{}' }))
		).rejects.toMatchObject({ status: 403 });
		expect(calls).toHaveLength(0);
	});

	it('refuses an unsafe method with no Origin at all', async () => {
		const { api, calls } = upstream(new Response('{}'));

		await expect(POST(event({ method: 'POST', api, body: '{}' }))).rejects.toMatchObject({
			status: 403
		});
		expect(calls).toHaveLength(0);
	});

	it('allows a GET without one, because a read is not a write', async () => {
		const { api } = upstream(new Response('{}'));
		const res = await GET(event({ api }));
		expect(res.status).toBe(200);
	});
});

describe('BFF proxy — when the API cannot answer', () => {
	it('says 502 in the API’s own error shape when it cannot be reached', async () => {
		const api = vi.fn(async () => {
			throw new TypeError('fetch failed');
		});
		const errors = vi.spyOn(console, 'error').mockImplementation(() => {});

		const res = await GET(event({ api }));

		expect(res.status).toBe(502);
		expect(res.headers.get('content-type')).toBe('application/problem+json');
		expect(res.headers.get('x-request-id')).toBe('trace-abc');
		expect((await res.json()).requestId).toBe('trace-abc');
		errors.mockRestore();
	});

	it('says 504 when it took too long to start answering', async () => {
		const api = vi.fn(async () => {
			throw new UpstreamTimeout(60_000);
		});
		const errors = vi.spyOn(console, 'error').mockImplementation(() => {});

		const res = await POST(
			event({ method: 'POST', api, headers: { origin: ORIGIN, 'content-type': 'application/json' }, body: '{}' })
		);

		expect(res.status).toBe(504);
		expect((await res.json()).title).toBe('The API took too long to answer');
		errors.mockRestore();
	});
});
