import { error } from '@sveltejs/kit';
import type { LinkContent } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const res = await locals.api(`/api/v1/links/${params.id}/content`);

	// 404 covers both "no such link" and "that page had no article in it" — from the reader's
	// side they are the same answer: there is nothing here to read.
	if (res.status === 404) throw error(404, 'No readable text was saved for that link');
	if (!res.ok) throw error(res.status, 'Failed to load the article');

	return { content: (await res.json()) as LinkContent };
};
