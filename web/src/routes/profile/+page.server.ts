import { redirect } from '@sveltejs/kit';
import type { PageServerLoad } from './$types';

// Settings lived here while the page was called Profile. Old bookmarks and links in sent emails
// still point at it.
export const load: PageServerLoad = () => {
	redirect(308, '/settings');
};
