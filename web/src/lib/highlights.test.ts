import { describe, expect, it } from 'vitest';
import { offsetIn, tidy, toSegments, type Highlight } from './highlights';

function mark(over: Partial<Highlight> = {}): Highlight {
	return {
		id: 'h1',
		linkId: 'l1',
		paragraphIndex: 0,
		start: 0,
		end: 4,
		text: '',
		note: null,
		createdAt: '2026-09-13T00:00:00Z',
		orphaned: false,
		...over
	};
}

describe('toSegments', () => {
	it('leaves an unmarked paragraph in one piece', () => {
		expect(toSegments('Hello there', [])).toEqual([{ text: 'Hello there', ids: [] }]);
	});

	it('cuts around a marked passage', () => {
		const segments = toSegments('Hello there friend', [mark({ start: 6, end: 11 })]);

		expect(segments).toEqual([
			{ text: 'Hello ', ids: [] },
			{ text: 'there', ids: ['h1'] },
			{ text: ' friend', ids: [] }
		]);
	});

	it('keeps a mark that starts at the first character', () => {
		expect(toSegments('Hello there', [mark({ start: 0, end: 5 })])).toEqual([
			{ text: 'Hello', ids: ['h1'] },
			{ text: ' there', ids: [] }
		]);
	});

	it('keeps a mark that runs to the end', () => {
		expect(toSegments('Hello there', [mark({ start: 6, end: 11 })])).toEqual([
			{ text: 'Hello ', ids: [] },
			{ text: 'there', ids: ['h1'] }
		]);
	});

	/**
	 * Marking a phrase inside a passage you already marked is a normal thing to do, and the
	 * overlap has to know it belongs to both or a click on it lands on the wrong one.
	 */
	it('covers an overlap with both marks', () => {
		const segments = toSegments('abcdefghij', [
			mark({ id: 'wide', start: 0, end: 8 }),
			mark({ id: 'narrow', start: 4, end: 6 })
		]);

		expect(segments.map((s) => s.text)).toEqual(['abcd', 'ef', 'gh', 'ij']);
		expect(segments[1].ids).toEqual(['wide', 'narrow']);
		expect(segments[0].ids).toEqual(['wide']);
		expect(segments[3].ids).toEqual([]);
	});

	it('puts the smallest mark last so a click can prefer it', () => {
		const segments = toSegments('abcdefgh', [
			mark({ id: 'narrow', start: 2, end: 4 }),
			mark({ id: 'wide', start: 0, end: 8 })
		]);

		expect(segments[1].ids).toEqual(['wide', 'narrow']);
	});

	it('ignores a mark whose words have moved', () => {
		const segments = toSegments('Hello there', [mark({ start: 0, end: 5, orphaned: true })]);

		expect(segments).toEqual([{ text: 'Hello there', ids: [] }]);
	});

	/** A shorter re-extraction would otherwise slice past the end of the string. */
	it('ignores a mark that runs off the end of the paragraph', () => {
		expect(toSegments('Short', [mark({ start: 0, end: 90 })])).toEqual([
			{ text: 'Short', ids: [] }
		]);
	});
});

describe('tidy', () => {
	it('drops the whitespace a drag picks up at either end', () => {
		expect(tidy('  hello  ', 0, 9)).toEqual({ start: 2, end: 7 });
	});

	it('leaves a clean selection alone', () => {
		expect(tidy('hello', 0, 5)).toEqual({ start: 0, end: 5 });
	});

	it('collapses a selection of nothing but whitespace', () => {
		const { start, end } = tidy('a   b', 1, 4);
		expect(start).toBe(end);
	});
});

describe('offsetIn', () => {
	it('reads an offset in a plain paragraph', () => {
		const p = document.createElement('p');
		p.textContent = 'Hello there';

		expect(offsetIn(p, p.firstChild!, 6)).toBe(6);
	});

	/** The paragraph stops being one text node the moment anything in it is marked. */
	it('adds up the runs before the one the selection landed in', () => {
		const p = document.createElement('p');
		p.innerHTML = 'Hello <mark>there</mark> friend';

		const tail = p.lastChild!;
		expect(tail.textContent).toBe(' friend');
		expect(offsetIn(p, tail, 1)).toBe(12);
	});

	it('reads a position given on an element rather than a text node', () => {
		const p = document.createElement('p');
		p.innerHTML = 'Hello <mark>there</mark> friend';

		// Two children in: after "Hello " and after the mark.
		expect(offsetIn(p, p, 2)).toBe(11);
	});

	it('refuses a node from another paragraph', () => {
		const p = document.createElement('p');
		p.textContent = 'Here';
		const other = document.createElement('p');
		other.textContent = 'Elsewhere';

		expect(offsetIn(p, other.firstChild!, 0)).toBeNull();
	});
});
