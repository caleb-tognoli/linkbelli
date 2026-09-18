/**
 * How the web server talks to the API: one trace per browser request, a deadline on every call,
 * and a readable answer when the API cannot be reached.
 */

/**
 * How long the API has to start answering.
 *
 * Time to the response headers only, not to the end of the body: an export of a large library
 * streams for as long as it takes, and cutting it off at a fixed length would break exactly the
 * downloads people most need. A request that has not even started answering in a minute is a
 * hung API, and the browser deserves to be told rather than left spinning.
 */
export const UPSTREAM_DEADLINE_MS = 60_000;

/**
 * A W3C trace context header for one browser request.
 *
 * The API names every response with its trace id and logs under it, and it adopts an incoming
 * trace rather than starting its own. So sending one of these makes the id the browser sees, the
 * web server's logs and the API's logs all the same string — which is the difference between a bug
 * report that names one request and one that says "around 14:32".
 */
export function newTraceparent(): { traceparent: string; traceId: string } {
	const traceId = hex(16);
	return { traceparent: `00-${traceId}-${hex(8)}-01`, traceId };
}

function hex(bytes: number): string {
	const buffer = new Uint8Array(bytes);
	crypto.getRandomValues(buffer);

	// All zeros is invalid for both ids, and vanishingly unlikely — but free to rule out.
	if (buffer.every((b) => b === 0)) buffer[bytes - 1] = 1;

	return Array.from(buffer, (b) => b.toString(16).padStart(2, '0')).join('');
}

/** Raised when the API took longer than the deadline to start answering. */
export class UpstreamTimeout extends Error {
	constructor(ms: number) {
		super(`The API did not start answering within ${Math.round(ms / 1000)} seconds.`);
		this.name = 'UpstreamTimeout';
	}
}

/**
 * fetch, abandoned if the response has not started within <paramref>ms</paramref>.
 *
 * The timer is cleared as soon as headers arrive, so the body can take as long as it needs. A
 * caller's own signal still works alongside it.
 */
export async function fetchWithDeadline(
	input: string,
	init: RequestInit = {},
	ms = UPSTREAM_DEADLINE_MS,
	fetchImpl: typeof fetch = fetch
): Promise<Response> {
	const controller = new AbortController();
	const timer = setTimeout(() => controller.abort(new UpstreamTimeout(ms)), ms);

	const outer = init.signal;
	const forward = () => controller.abort(outer?.reason);
	outer?.addEventListener('abort', forward, { once: true });

	try {
		return await fetchImpl(input, { ...init, signal: controller.signal });
	} catch (error) {
		if (controller.signal.reason instanceof UpstreamTimeout) throw controller.signal.reason;
		throw error;
	} finally {
		clearTimeout(timer);
		outer?.removeEventListener('abort', forward);
	}
}

/**
 * What the browser gets when the API could not answer at all.
 *
 * Problem Details, the same shape the API itself uses for errors, so the client has one kind of
 * error body to read rather than the API's for some failures and SvelteKit's for others. The
 * request id is in it and in the headers, so "it said 502" comes with something to look up.
 */
export function upstreamFailure(error: unknown, requestId: string): Response {
	const timedOut = error instanceof UpstreamTimeout;
	const status = timedOut ? 504 : 502;

	const body = {
		type: 'about:blank',
		title: timedOut ? 'The API took too long to answer' : 'The API could not be reached',
		status,
		detail: timedOut
			? (error as Error).message
			: 'The web server could not connect to the API. It may be restarting — try again shortly.',
		requestId
	};

	return new Response(JSON.stringify(body), {
		status,
		headers: {
			'content-type': 'application/problem+json',
			'x-request-id': requestId,
			// A moment, not a number worked out from anything: long enough for a restart.
			'retry-after': '5'
		}
	});
}
