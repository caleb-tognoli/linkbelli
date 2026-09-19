import type { LayoutServerLoad } from './$types';
import type { Feed, Folder, PinnedSearch, User } from '$lib/types';

export const load: LayoutServerLoad = async ({ locals, url }) => {
	// An embed renders none of the chrome these feed, so it does not pay for them either.
	if (!locals.authenticated || url.pathname.startsWith('/embed/')) {
		return { user: null, folders: [] as Folder[], pinned: [] as PinnedSearch[], feedNew: 0 };
	}

	// Folders come along with the layout so the sidebar can show the whole tree. They are a flat
	// list of at most a few dozen rows; fetching them per page beats making every folder page
	// discover its own ancestors.
	const [meRes, foldersRes, pinnedRes, feedRes] = await Promise.all([
		locals.api('/api/v1/me'),
		locals.api('/api/v1/folders'),
		// A saved search with a count beside it is something you glance at; without one it is a
		// link. Capped at five on the API side, because each is a count query and this request
		// happens on every navigation.
		locals.api('/api/v1/search/saved/pinned'),
		// Only the count is used: how much the feed has brought since it was last opened.
		locals.api('/api/v1/feed?limit=1')
	]);

	if (!meRes.ok) {
		return { user: null, folders: [] as Folder[], pinned: [] as PinnedSearch[], feedNew: 0 };
	}

	const user = (await meRes.json()) as User;
	const folders = foldersRes.ok ? ((await foldersRes.json()) as Folder[]) : [];
	const pinned = pinnedRes.ok ? ((await pinnedRes.json()) as PinnedSearch[]) : [];
	const feedNew = feedRes.ok ? ((await feedRes.json()) as Feed).newCount : 0;

	return { user, folders, pinned, feedNew };
};
