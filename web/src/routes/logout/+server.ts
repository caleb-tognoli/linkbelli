import { redirect } from '@sveltejs/kit';
import { clearTokens } from '$lib/server/auth';
import type { RequestHandler } from './$types';

export const POST: RequestHandler = ({ cookies }) => {
	clearTokens(cookies);
	throw redirect(303, '/login');
};
