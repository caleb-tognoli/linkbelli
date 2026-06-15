import type { Folder, FolderDetail } from '$lib/types';

/** The server-side API caller exposed on `event.locals` by hooks.server.ts. */
export type ApiFetch = App.Locals['api'];

async function json<T>(res: Response, what: string): Promise<T> {
	if (!res.ok) throw new Error(`${what} failed (${res.status})`);
	return res.json() as Promise<T>;
}

export function listFolders(api: ApiFetch): Promise<Folder[]> {
	return api('/api/v1/folders').then((r) => json<Folder[]>(r, 'List folders'));
}

export function getFolder(api: ApiFetch, id: string): Promise<Response> {
	return api(`/api/v1/folders/${id}`);
}

export type { FolderDetail };
