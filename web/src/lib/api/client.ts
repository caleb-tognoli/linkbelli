// Browser-side API client. Calls the same-origin BFF proxy (/api/v1/*), which injects the bearer
// token server-side. Returns the raw Response so callers can branch on status (e.g. 409 dup).

function send(method: string, path: string, body?: unknown): Promise<Response> {
	return fetch(`/api/v1${path}`, {
		method,
		headers: body !== undefined ? { 'content-type': 'application/json' } : undefined,
		body: body !== undefined ? JSON.stringify(body) : undefined
	});
}

export const api = {
	get: (path: string) => send('GET', path),
	post: (path: string, body?: unknown) => send('POST', path, body),
	patch: (path: string, body?: unknown) => send('PATCH', path, body),
	put: (path: string, body?: unknown) => send('PUT', path, body),
	del: (path: string) => send('DELETE', path)
};

/** Parse JSON or throw a readable error. */
export async function json<T>(res: Response): Promise<T> {
	if (!res.ok) throw new Error(`Request failed (${res.status})`);
	return res.json() as Promise<T>;
}
