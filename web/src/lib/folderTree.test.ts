import { describe, expect, it } from 'vitest';
import { buildFolderTree, flatten, pathTo } from './folderTree';
import type { Folder } from './types';

function folder(id: string, name: string, parentId: string | null = null): Folder {
	return { id, name, parentId, subfolderCount: 0, playlistCount: 0 } as Folder;
}

const tree = [
	folder('reading', 'Reading'),
	folder('tech', 'Tech', 'reading'),
	folder('databases', 'Databases', 'tech'),
	folder('watching', 'Watching'),
	folder('archive', 'Archive')
];

describe('buildFolderTree', () => {
	it('nests children under their parent', () => {
		const roots = buildFolderTree(tree);

		expect(roots.map((r) => r.id)).toEqual(['archive', 'reading', 'watching']);

		const reading = roots.find((r) => r.id === 'reading')!;
		expect(reading.children.map((c) => c.id)).toEqual(['tech']);
		expect(reading.children[0].children.map((c) => c.id)).toEqual(['databases']);
	});

	it('records how deep each folder sits, for indentation', () => {
		const roots = buildFolderTree(tree);
		const reading = roots.find((r) => r.id === 'reading')!;

		expect(reading.depth).toBe(0);
		expect(reading.children[0].depth).toBe(1);
		expect(reading.children[0].children[0].depth).toBe(2);
	});

	it('sorts siblings by name at every level', () => {
		const roots = buildFolderTree([
			folder('a', 'Zebra'),
			folder('b', 'Apple'),
			folder('c', 'Mango', 'b'),
			folder('d', 'Banana', 'b')
		]);

		expect(roots.map((r) => r.name)).toEqual(['Apple', 'Zebra']);
		expect(roots[0].children.map((c) => c.name)).toEqual(['Banana', 'Mango']);
	});

	it('treats a folder with a missing parent as a root rather than losing it', () => {
		// Dropping it would hide everything filed underneath it.
		const roots = buildFolderTree([folder('orphan', 'Orphan', 'gone')]);

		expect(roots.map((r) => r.id)).toEqual(['orphan']);
	});

	it('handles an empty list', () => {
		expect(buildFolderTree([])).toEqual([]);
	});

	it('does not mutate the folders it was given', () => {
		const input = [folder('a', 'A'), folder('b', 'B', 'a')];
		buildFolderTree(input);

		expect(input[0]).not.toHaveProperty('children');
	});
});

describe('pathTo', () => {
	it('walks from the root down to the folder', () => {
		expect(pathTo(tree, 'databases').map((f) => f.id)).toEqual(['reading', 'tech', 'databases']);
	});

	it('is just the folder itself at the root', () => {
		expect(pathTo(tree, 'watching').map((f) => f.id)).toEqual(['watching']);
	});

	it('is empty for nothing selected, or for an unknown folder', () => {
		expect(pathTo(tree, null)).toEqual([]);
		expect(pathTo(tree, 'nonexistent')).toEqual([]);
	});

	it('terminates on a cycle rather than looping forever', () => {
		// Not reachable through the API, which rejects cycles — but a walk up parent links
		// should not be the thing that hangs if one ever existed.
		const cyclic = [folder('a', 'A', 'b'), folder('b', 'B', 'a')];

		expect(pathTo(cyclic, 'a').length).toBeLessThanOrEqual(3);
	});
});

describe('flatten', () => {
	it('shows only what is open', () => {
		const roots = buildFolderTree(tree);

		const closed = flatten(roots, new Set());
		expect(closed.map((n) => n.id)).toEqual(['archive', 'reading', 'watching']);

		const open = flatten(roots, new Set(['reading']));
		expect(open.map((n) => n.id)).toEqual(['archive', 'reading', 'tech', 'watching']);
	});

	it('follows an open branch all the way down', () => {
		const roots = buildFolderTree(tree);

		const open = flatten(roots, new Set(['reading', 'tech']));
		expect(open.map((n) => n.id)).toEqual(['archive', 'reading', 'tech', 'databases', 'watching']);
	});

	it('keeps a child hidden when its parent is closed, however open it is', () => {
		const roots = buildFolderTree(tree);

		const open = flatten(roots, new Set(['tech']));
		expect(open.map((n) => n.id)).not.toContain('databases');
	});
});
