import { error } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

// Authenticated BFF proxy: the browser calls same-origin /api/v1/* and this forwards to the real
// API with the bearer token from the httpOnly cookie (via locals.api, which also refreshes on 401).
// Tokens never reach the browser. SameSite=Lax already blocks cross-site cookie use; we additionally
// require a matching Origin on unsafe methods as defense-in-depth against CSRF.
const handler: RequestHandler = async ({ params, request, url, locals }) => {
	if (request.method !== 'GET' && request.headers.get('origin') !== url.origin) {
		throw error(403, 'Bad origin');
	}

	const init: RequestInit = { method: request.method };
	if (request.method !== 'GET' && request.method !== 'HEAD') {
		init.body = await request.text();
		const contentType = request.headers.get('content-type');
		if (contentType) init.headers = { 'content-type': contentType };
	}

	const res = await locals.api(`/api/v1/${params.path}${url.search}`, init);

	return new Response(res.status === 204 ? null : res.body, {
		status: res.status,
		headers: { 'content-type': res.headers.get('content-type') ?? 'application/json' }
	});
};

export const GET = handler;
export const POST = handler;
export const PATCH = handler;
export const PUT = handler;
export const DELETE = handler;
