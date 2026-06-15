import { error } from '@sveltejs/kit';
import { getFolder } from '$lib/api/folders';
import type { FolderDetail } from '$lib/types';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ locals, params }) => {
	const res = await getFolder(locals.api, params.id);
	if (res.status === 404) throw error(404, 'Folder not found');
	if (!res.ok) throw error(res.status, 'Failed to load folder');

	const folder = (await res.json()) as FolderDetail;
	return { folder };
};
