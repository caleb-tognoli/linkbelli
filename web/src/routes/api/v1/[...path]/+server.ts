import { error } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

/**
 * Headers copied from the browser's request up to the API.
 *
 * An allowlist rather than a blanket copy: `cookie` carries the session this proxy exists to keep
 * away from the API, `host` would name the wrong server, and `authorization` must come from the
 * httpOnly cookie rather than from anything the page can set.
 */
const FORWARD_REQUEST = [
	'content-type',
	'accept',
	'accept-language',
	// Conditional GET. Without these the API's ETags can never produce a 304, which is most of
	// what they are for.
	'if-none-match',
	'if-modified-since',
	// Lets a client make a POST safe to retry. The API has supported this all along; nothing
	// could use it while this proxy dropped the header.
	'idempotency-key'
];

/**
 * Headers copied from the API's response back down to the browser.
 *
 * This used to be `content-type` and nothing else, which quietly disabled three separate API
 * features: `etag` (so conditional GETs never revalidated), `content-disposition` (so every
 * export and backup downloaded under a URL-derived name), and `cache-control` (so thumbnails
 * were refetched on every page load). `set-cookie` is deliberately absent — the API has no
 * business setting cookies on this origin.
 */
const FORWARD_RESPONSE = [
	'content-type',
	'content-disposition',
	'cache-control',
	'etag',
	'last-modified',
	'vary',
	'location',
	// So a client can say when to try again instead of guessing.
	'retry-after',
	'content-language'
];

/** Methods that carry no body, whatever the caller sent. */
const BODILESS = new Set(['GET', 'HEAD']);

// Authenticated BFF proxy: the browser calls same-origin /api/v1/* and this forwards to the real
// API with the bearer token from the httpOnly cookie (via locals.api, which also refreshes on 401).
// Tokens never reach the browser. SameSite=Lax already blocks cross-site cookie use; we additionally
// require a matching Origin on unsafe methods as defense-in-depth against CSRF.
const handler: RequestHandler = async ({ params, request, url, locals, getClientAddress }) => {
	if (request.method !== 'GET' && request.headers.get('origin') !== url.origin) {
		throw error(403, 'Bad origin');
	}

	const headers = new Headers();
	for (const name of FORWARD_REQUEST) {
		const value = request.headers.get(name);
		if (value !== null) headers.set(name, value);
	}

	// Every request the API sees from here arrives from one container, so without this its rate
	// limiter buckets the whole anonymous internet together. Appended rather than set, so a proxy
	// in front of this app keeps its place in the chain.
	const forwardedFor = request.headers.get('x-forwarded-for');
	headers.set('x-forwarded-for', forwardedFor ? `${forwardedFor}, ${getClientAddress()}` : getClientAddress());
	headers.set('x-forwarded-proto', url.protocol.replace(':', ''));

	const init: RequestInit = { method: request.method, headers };
	if (!BODILESS.has(request.method)) {
		// Bytes rather than `await request.text()`, which decodes as UTF-8 and so corrupts any
		// body that is not text. Buffered rather than streamed on purpose: locals.api replays the
		// request after refreshing an expired token, and a stream cannot be sent twice.
		const body = await request.arrayBuffer();
		// A DELETE with no body should carry none, rather than an empty one that makes the
		// caller below decide it needs a content-type.
		if (body.byteLength > 0) init.body = body;
	}

	const res = await locals.api(`/api/v1/${params.path}${url.search}`, init);

	const responseHeaders = new Headers();
	for (const name of FORWARD_RESPONSE) {
		const value = res.headers.get(name);
		if (value !== null) responseHeaders.set(name, value);
	}
	if (!responseHeaders.has('content-type')) {
		responseHeaders.set('content-type', 'application/json');
	}

	// 204 and 304 must not carry one, and Response rejects a body on either.
	const bodyless = res.status === 204 || res.status === 304;

	return new Response(bodyless ? null : res.body, {
		status: res.status,
		headers: responseHeaders
	});
};

export const GET = handler;
export const POST = handler;
export const PATCH = handler;
export const PUT = handler;
export const DELETE = handler;
