// Browser-side API client. Calls the same-origin BFF proxy (/api/v1/*), which injects the bearer
// token server-side. Returns the raw Response so callers can branch on status (e.g. 409 dup).

/**
 * The status a request that never reached the server is reported as.
 *
 * fetch rejects when there is no connection, or the name does not resolve, or the tab is closing
 * mid-request — it does not resolve with a failing response. Nearly every call site here wraps a
 * mutation in `try { … } finally { busy = false }` rather than a catch, so that rejection escaped:
 * the spinner stopped, no toast appeared, and optimistic state was left on screen showing a change
 * that had not happened. Renaming a playlist offline left the new name in the box and lost it on
 * the next load.
 *
 * So a dropped request is answered rather than thrown, and every existing `if (!res.ok)` branch
 * handles being offline for free.
 */
export const NO_CONNECTION = 0;

/** A response for a request that never left. `Response.error()` is exactly this, where it exists. */
function noConnection(): Response {
	// jsdom has had Response without the static error() at times; the shape is what matters.
	if (typeof Response.error === 'function') return Response.error();

	return new Response(null, { status: 503, statusText: 'No connection' });
}

async function send(method: string, path: string, body?: unknown): Promise<Response> {
	try {
		return await fetch(`/api/v1${path}`, {
			method,
			headers: body !== undefined ? { 'content-type': 'application/json' } : undefined,
			body: body !== undefined ? JSON.stringify(body) : undefined
		});
	} catch {
		return noConnection();
	}
}

export const api = {
	get: (path: string) => send('GET', path),
	post: (path: string, body?: unknown) => send('POST', path, body),
	patch: (path: string, body?: unknown) => send('PATCH', path, body),
	put: (path: string, body?: unknown) => send('PUT', path, body),
	// A body, because closing an account is confirmed by password and the method is still DELETE.
	del: (path: string, body?: unknown) => send('DELETE', path, body)
};

/** Whether a response is one that never reached the server. */
export function isOffline(res: Response): boolean {
	return res.status === NO_CONNECTION || res.type === 'error';
}

/** Parse JSON or throw a readable error. */
export async function json<T>(res: Response): Promise<T> {
	if (!res.ok) throw new Error(`Request failed (${res.status})`);
	return res.json() as Promise<T>;
}
