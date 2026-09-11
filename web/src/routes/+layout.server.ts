import type { LayoutServerLoad } from './$types';
import type { Folder, User } from '$lib/types';

export const load: LayoutServerLoad = async ({ locals }) => {
	if (!locals.authenticated) {
		return { user: null, folders: [] as Folder[] };
	}

	// Folders come along with the layout so the sidebar can show the whole tree. They are a flat
	// list of at most a few dozen rows; fetching them per page beats making every folder page
	// discover its own ancestors.
	const [meRes, foldersRes] = await Promise.all([
		locals.api('/api/v1/me'),
		locals.api('/api/v1/folders')
	]);

	if (!meRes.ok) {
		return { user: null, folders: [] as Folder[] };
	}

	const user = (await meRes.json()) as User;
	const folders = foldersRes.ok ? ((await foldersRes.json()) as Folder[]) : [];

	return { user, folders };
};
