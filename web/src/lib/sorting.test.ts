import { describe, expect, it } from 'vitest';
import {
	canReorder,
	modeToServerSort,
	nextDateSort,
	nextScoreSort,
	orderForDisplay,
	serverSortToMode,
	type SortMode
} from './sorting';
import type { PlaylistItem } from './types';

function item(id: string, creationTime: string): PlaylistItem {
	return {
		id,
		position: 0,
		note: null,
		status: 'Added',
		creationTime,
		score: null,
		sourceId: null,
		metadata: null,
		link: {
			id: `link-${id}`,
			url: `https://example.com/${id}`,
			host: 'example.com',
			title: id,
			description: null,
			thumbnailUrl: null,
			siteName: null,
			enriched: true,
			nsfw: false,
			favicon: null,
			enrichmentStatus: 'Succeeded',
			enrichmentError: null
		}
	} as PlaylistItem;
}

describe('serverSortToMode', () => {
	it.each([
		['date-asc', 'date-asc'],
		['date-desc', 'date-desc'],
		['shuffle', 'shuffle'],
		['score-asc', 'score-asc'],
		['score-desc', 'score-desc']
	])('maps %s through unchanged', (stored, expected) => {
		expect(serverSortToMode(stored, false)).toBe(expected);
	});

	it('falls back to the playlist order for someone who can rearrange it', () => {
		expect(serverSortToMode(undefined, false)).toBe('manual');
		expect(serverSortToMode('position', false)).toBe('manual');
		expect(serverSortToMode('nonsense', false)).toBe('manual');
	});

	it('falls back to newest for a viewer, who has no manual order', () => {
		expect(serverSortToMode(undefined, true)).toBe('date-desc');
		expect(serverSortToMode('position', true)).toBe('date-desc');
	});
});

describe('modeToServerSort', () => {
	it('calls the playlist order by the name the API uses', () => {
		expect(modeToServerSort('manual')).toBe('position');
	});

	it.each<SortMode>(['date-asc', 'date-desc', 'shuffle', 'score-asc', 'score-desc'])(
		'passes %s through',
		(mode) => {
			expect(modeToServerSort(mode)).toBe(mode);
		}
	);
});

describe('header click cycles', () => {
	it('cycles the date header newest → oldest → playlist order', () => {
		expect(nextDateSort('manual', false)).toBe('date-desc');
		expect(nextDateSort('date-desc', false)).toBe('date-asc');
		expect(nextDateSort('date-asc', false)).toBe('manual');
	});

	it('never returns a viewer to an order they cannot see', () => {
		// A viewer has no manual order, so the third click wraps to newest instead.
		expect(nextDateSort('date-asc', true)).toBe('date-desc');
		expect(nextScoreSort('score-asc', true)).toBe('score-desc');
	});

	it('cycles the score header highest → lowest → playlist order', () => {
		expect(nextScoreSort('manual', false)).toBe('score-desc');
		expect(nextScoreSort('score-desc', false)).toBe('score-asc');
		expect(nextScoreSort('score-asc', false)).toBe('manual');
	});

	it('starts a fresh cycle when arriving from an unrelated sort', () => {
		expect(nextDateSort('score-desc', false)).toBe('date-desc');
		expect(nextScoreSort('date-asc', false)).toBe('score-desc');
	});
});

describe('orderForDisplay', () => {
	const items = [
		item('b', '2026-03-02T00:00:00Z'),
		item('a', '2026-03-01T00:00:00Z'),
		item('c', '2026-03-03T00:00:00Z')
	];

	it('sorts oldest first', () => {
		expect(orderForDisplay(items, 'date-asc').map((i) => i.id)).toEqual(['a', 'b', 'c']);
	});

	it('sorts newest first', () => {
		expect(orderForDisplay(items, 'date-desc').map((i) => i.id)).toEqual(['c', 'b', 'a']);
	});

	it('does not mutate the list it was given', () => {
		orderForDisplay(items, 'date-asc');
		expect(items.map((i) => i.id)).toEqual(['b', 'a', 'c']);
	});

	it.each<SortMode>(['manual', 'shuffle', 'score-asc', 'score-desc'])(
		'leaves %s to the server, whose order is canonical',
		(mode) => {
			expect(orderForDisplay(items, mode)).toBe(items);
		}
	);

	it('handles an empty list', () => {
		expect(orderForDisplay([], 'date-asc')).toEqual([]);
	});
});

describe('canReorder', () => {
	it('allows dragging only against the playlist own order', () => {
		expect(canReorder('manual', false)).toBe(true);
		expect(canReorder('date-desc', false)).toBe(false);
		expect(canReorder('shuffle', false)).toBe(false);
	});

	it('never allows a viewer to drag', () => {
		expect(canReorder('manual', true)).toBe(false);
	});
});
