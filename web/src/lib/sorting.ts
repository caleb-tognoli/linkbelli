import type { PlaylistItem } from '$lib/types';

/**
 * How the item table is ordered. `manual` is the playlist's own order, which only the owner can
 * rearrange; the rest map onto a server sort.
 */
export type SortMode = 'manual' | 'date-asc' | 'date-desc' | 'shuffle' | 'score-asc' | 'score-desc';

export const SORT_LABELS: Record<SortMode, string> = {
	manual: 'Manual',
	'date-asc': 'Oldest',
	'date-desc': 'Newest',
	shuffle: 'Shuffle',
	'score-asc': 'Score ↑',
	'score-desc': 'Score ↓'
};

/**
 * The mode a stored preference means. A viewer who can't rearrange has no manual order to fall
 * back to, so an unrecognised preference lands on newest-first for them.
 */
export function serverSortToMode(stored: string | undefined, readonly: boolean): SortMode {
	if (stored === 'date-asc') return 'date-asc';
	if (stored === 'date-desc') return 'date-desc';
	if (stored === 'shuffle') return 'shuffle';
	if (stored === 'score-asc') return 'score-asc';
	if (stored === 'score-desc') return 'score-desc';
	return readonly ? 'date-desc' : 'manual';
}

/** What to ask the API for. `manual` is the playlist's stored order, which the API calls position. */
export function modeToServerSort(mode: SortMode): string {
	return mode === 'manual' ? 'position' : mode;
}

/**
 * Clicking the "Added" header cycles newest → oldest → off. "Off" is the playlist's own order
 * for its owner, and newest-first for a viewer, who has no manual order to return to.
 */
export function nextDateSort(current: SortMode, readonly: boolean): SortMode {
	if (current === 'date-desc') return 'date-asc';
	if (current === 'date-asc') return readonly ? 'date-desc' : 'manual';
	return 'date-desc';
}

/** The same cycle for the score header. */
export function nextScoreSort(current: SortMode, readonly: boolean): SortMode {
	if (current === 'score-desc') return 'score-asc';
	if (current === 'score-asc') return readonly ? 'score-desc' : 'manual';
	return 'score-desc';
}

/**
 * The order to render in. Date sorts are applied client-side so a newly added item lands in the
 * right place without a refetch; every other mode takes the server's order as canonical, because
 * shuffle and score depend on state the client doesn't have.
 */
export function orderForDisplay(items: PlaylistItem[], mode: SortMode): PlaylistItem[] {
	if (mode === 'date-asc') return sortByDate(items, true);
	if (mode === 'date-desc') return sortByDate(items, false);
	return items;
}

function sortByDate(items: PlaylistItem[], ascending: boolean): PlaylistItem[] {
	return [...items].sort((a, b) => {
		const difference = new Date(a.creationTime).getTime() - new Date(b.creationTime).getTime();
		return ascending ? difference : -difference;
	});
}

/** Dragging to reorder only makes sense against the playlist's own order. */
export function canReorder(mode: SortMode, readonly: boolean): boolean {
	return !readonly && mode === 'manual';
}
