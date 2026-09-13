import type { LayoutServerLoad } from './$types';
import type { Folder, PinnedSearch, User } from '$lib/types';

export const load: LayoutServerLoad = async ({ locals }) => {
	if (!locals.authenticated) {
		return { user: null, folders: [] as Folder[], pinned: [] as PinnedSearch[] };
	}

	// Folders come along with the layout so the sidebar can show the whole tree. They are a flat
	// list of at most a few dozen rows; fetching them per page beats making every folder page
	// discover its own ancestors.
	const [meRes, foldersRes, pinnedRes] = await Promise.all([
		locals.api('/api/v1/me'),
		locals.api('/api/v1/folders'),
		// A saved search with a count beside it is something you glance at; without one it is a
		// link. Capped at five on the API side, because each is a count query and this request
		// happens on every navigation.
		locals.api('/api/v1/search/saved/pinned')
	]);

	if (!meRes.ok) {
		return { user: null, folders: [] as Folder[], pinned: [] as PinnedSearch[] };
	}

	const user = (await meRes.json()) as User;
	const folders = foldersRes.ok ? ((await foldersRes.json()) as Folder[]) : [];
	const pinned = pinnedRes.ok ? ((await pinnedRes.json()) as PinnedSearch[]) : [];

	return { user, folders, pinned };
};
