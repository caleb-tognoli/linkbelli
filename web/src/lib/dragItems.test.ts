import { describe, expect, it } from 'vitest';
import {
	bulkActionFor,
	decodePayload,
	describeDrag,
	describeResult,
	dragSet,
	dropMode,
	encodePayload
} from './dragItems';

describe('dragSet', () => {
	it('takes the whole selection when the dragged row is part of it', () => {
		// Moving one of forty visibly checked rows would be a silent surprise.
		expect(dragSet('b', ['a', 'b', 'c'])).toEqual(['a', 'b', 'c']);
	});

	it('takes only the dragged row when it is not selected', () => {
		// Reaching for an unchecked thing says nothing about the checked ones.
		expect(dragSet('z', ['a', 'b'])).toEqual(['z']);
	});

	it('takes the dragged row when nothing is selected', () => {
		expect(dragSet('a', [])).toEqual(['a']);
	});
});

describe('dropMode', () => {
	it('moves by default', () => {
		expect(dropMode({})).toBe('move');
	});

	it('copies with the modifiers people already use', () => {
		expect(dropMode({ ctrlKey: true })).toBe('copy');
		expect(dropMode({ altKey: true })).toBe('copy');
		expect(dropMode({ metaKey: true })).toBe('copy');
	});
});

describe('bulkActionFor', () => {
	it('names the action the API expects', () => {
		expect(bulkActionFor('move')).toBe('Move');
		expect(bulkActionFor('copy')).toBe('Copy');
	});
});

describe('payload round-trip', () => {
	it('survives being encoded and read back', () => {
		const payload = { fromPlaylistId: 'p1', itemIds: ['a', 'b'], label: '2 links' };

		expect(decodePayload(encodePayload(payload))).toEqual(payload);
	});

	it('refuses anything that is not one of our drags', () => {
		// A drop handler sees whatever the OS hands it: a file, a URL, a line of text.
		expect(decodePayload(null)).toBeNull();
		expect(decodePayload('')).toBeNull();
		expect(decodePayload('https://example.com')).toBeNull();
		expect(decodePayload('{"nope":1}')).toBeNull();
		expect(decodePayload('[1,2,3]')).toBeNull();
		expect(decodePayload('null')).toBeNull();
	});

	it('refuses a payload with nothing in it', () => {
		expect(decodePayload('{"fromPlaylistId":"p1","itemIds":[]}')).toBeNull();
		expect(decodePayload('{"fromPlaylistId":"","itemIds":["a"]}')).toBeNull();
		expect(decodePayload('{"fromPlaylistId":"p1","itemIds":["a",""]}')).toBeNull();
		expect(decodePayload('{"fromPlaylistId":"p1","itemIds":[1,2]}')).toBeNull();
	});

	it('fills in a label rather than failing without one', () => {
		expect(decodePayload('{"fromPlaylistId":"p1","itemIds":["a","b"]}')?.label).toBe('2 links');
	});
});

describe('describeDrag', () => {
	it('names the one thing being dragged', () => {
		expect(describeDrag(1, 'A long article title')).toBe('A long article title');
	});

	it('counts when there is more than one', () => {
		expect(describeDrag(4, 'ignored')).toBe('4 links');
	});

	it('falls back when the one thing has no title', () => {
		expect(describeDrag(1, null)).toBe('1 link');
		expect(describeDrag(1, '   ')).toBe('1 link');
	});
});

describe('describeResult', () => {
	it('says what happened', () => {
		expect(describeResult('move', 3, 0, 'Reading')).toBe('Moved 3 links to Reading.');
		expect(describeResult('copy', 1, 0, 'Reading')).toBe('Copied 1 link to Reading.');
	});

	it('mentions the ones already there without calling it a failure', () => {
		expect(describeResult('move', 2, 1, 'Reading')).toBe(
			'Moved 2 links to Reading. · 1 already there'
		);
	});

	it('explains a drop that did nothing', () => {
		// Nothing moved because it was all there already — which is an answer, not an error.
		expect(describeResult('move', 0, 3, 'Reading')).toBe('Already in Reading.');
		expect(describeResult('copy', 0, 0, 'Reading')).toBe('Nothing to copy.');
	});
});
