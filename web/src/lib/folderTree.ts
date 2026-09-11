import type { Folder } from '$lib/types';

/** A folder with its children resolved, so a sidebar can render the whole shape at once. */
export interface FolderNode extends Folder {
	children: FolderNode[];
	depth: number;
}

/**
 * Builds the tree from the flat list the API returns. Folders nest ten deep, and walking one
 * `/folders/{id}` page at a time was the only way to see past the first level.
 */
export function buildFolderTree(folders: Folder[]): FolderNode[] {
	const byId = new Map<string, FolderNode>(
		folders.map((folder) => [folder.id, { ...folder, children: [], depth: 0 }])
	);

	const roots: FolderNode[] = [];

	for (const node of byId.values()) {
		const parent = node.parentId ? byId.get(node.parentId) : undefined;

		// A folder whose parent isn't in the list is treated as a root rather than dropped —
		// losing it would hide everything filed underneath it.
		if (parent) parent.children.push(node);
		else roots.push(node);
	}

	assignDepth(roots, 0);
	sortByName(roots);

	return roots;
}

/**
 * The path from a root down to one folder, for breadcrumbs and for knowing which branches of a
 * sidebar to leave open. Empty when the id isn't in the tree.
 */
export function pathTo(folders: Folder[], id: string | null): Folder[] {
	if (!id) return [];

	const byId = new Map(folders.map((folder) => [folder.id, folder]));
	const path: Folder[] = [];

	let current = byId.get(id);
    // Bounded by the list length: a cycle in the data would otherwise loop forever here.
	const guard = folders.length + 1;

	for (let step = 0; current && step < guard; step++) {
		path.unshift(current);
		current = current.parentId ? byId.get(current.parentId) : undefined;
	}

	return path;
}

/** Flattens the tree in render order, so a sidebar can map over one array. */
export function flatten(nodes: FolderNode[], openIds: ReadonlySet<string>): FolderNode[] {
	const rows: FolderNode[] = [];

	for (const node of nodes) {
		rows.push(node);
		if (openIds.has(node.id)) {
			rows.push(...flatten(node.children, openIds));
		}
	}

	return rows;
}

function assignDepth(nodes: FolderNode[], depth: number): void {
	for (const node of nodes) {
		node.depth = depth;
		assignDepth(node.children, depth + 1);
	}
}

function sortByName(nodes: FolderNode[]): void {
	nodes.sort((a, b) => a.name.localeCompare(b.name));
	for (const node of nodes) sortByName(node.children);
}
