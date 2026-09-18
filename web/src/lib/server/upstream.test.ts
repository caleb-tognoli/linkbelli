import { afterEach, describe, expect, it, vi } from 'vitest';
import {
	UpstreamTimeout,
	fetchWithDeadline,
	newTraceparent,
	upstreamFailure
} from './upstream';

afterEach(() => {
	vi.useRealTimers();
});

describe('newTraceparent', () => {
	it('is a valid W3C trace context header', () => {
		const { traceparent, traceId } = newTraceparent();

		expect(traceparent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
		expect(traceparent.split('-')[1]).toBe(traceId);
	});

	it('is different every time', () => {
		const ids = new Set(Array.from({ length: 50 }, () => newTraceparent().traceId));
		expect(ids.size).toBe(50);
	});
});

describe('fetchWithDeadline', () => {
	it('hands back the response when the API answers in time', async () => {
		const reply = new Response('ok');
		const res = await fetchWithDeadline('http://api/x', {}, 1000, async () => reply);

		expect(res).toBe(reply);
	});

	it('gives up on an API that never starts answering', async () => {
		vi.useFakeTimers();
		const hang: typeof fetch = (_input, init) =>
			new Promise((_resolve, reject) => {
				init?.signal?.addEventListener('abort', () => reject(init.signal!.reason));
			});

		const pending = fetchWithDeadline('http://api/x', {}, 1000, hang);
		const assertion = expect(pending).rejects.toBeInstanceOf(UpstreamTimeout);
		await vi.advanceTimersByTimeAsync(1000);

		await assertion;
	});

	/** The deadline is for the answer to start. A long download must not be cut off halfway. */
	it('does not time out the body once the headers have arrived', async () => {
		vi.useFakeTimers();
		let signal: AbortSignal | undefined;
		const fetchImpl: typeof fetch = async (_input, init) => {
			signal = init?.signal ?? undefined;
			return new Response('streaming…');
		};

		await fetchWithDeadline('http://api/x', {}, 1000, fetchImpl);
		await vi.advanceTimersByTimeAsync(5000);

		expect(signal?.aborted).toBe(false);
	});

	it('still honours the caller’s own signal', async () => {
		const outer = new AbortController();
		const hang: typeof fetch = (_input, init) =>
			new Promise((_resolve, reject) => {
				init?.signal?.addEventListener('abort', () => reject(new Error('aborted by caller')));
			});

		const pending = fetchWithDeadline('http://api/x', { signal: outer.signal }, 60_000, hang);
		outer.abort();

		await expect(pending).rejects.toThrow('aborted by caller');
	});

	it('lets a connection failure through as itself', async () => {
		const refused: typeof fetch = async () => {
			throw new TypeError('fetch failed');
		};

		await expect(fetchWithDeadline('http://api/x', {}, 1000, refused)).rejects.toThrow('fetch failed');
	});
});

describe('upstreamFailure', () => {
	it('says 504 for an API that took too long', async () => {
		const res = upstreamFailure(new UpstreamTimeout(60_000), 'abc123');
		const body = await res.json();

		expect(res.status).toBe(504);
		expect(res.headers.get('content-type')).toBe('application/problem+json');
		expect(res.headers.get('x-request-id')).toBe('abc123');
		expect(body.requestId).toBe('abc123');
		expect(body.detail).toContain('60 seconds');
	});

	it('says 502 for an API that could not be reached', async () => {
		const res = upstreamFailure(new TypeError('fetch failed'), 'abc123');

		expect(res.status).toBe(502);
		expect((await res.json()).title).toBe('The API could not be reached');
		expect(res.headers.get('retry-after')).toBe('5');
	});
});
