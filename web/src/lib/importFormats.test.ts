import { describe, expect, it } from 'vitest';
import { parseRows } from './importFormats';

/**
 * What the importer will take.
 *
 * The home page promised bookmark exports and plain lists of addresses; the importer read CSV
 * and nothing else — including this app's own HTML export, which it offered and could not read.
 */
describe('parseRows', () => {
	it('reads a CSV with a url column and an optional note', () => {
		const rows = parseRows('url,note\nhttps://example.com/a,Worth keeping\nhttps://example.com/b,\n', 'links.csv');

		expect(rows).toEqual([
			{ url: 'https://example.com/a', note: 'Worth keeping' },
			{ url: 'https://example.com/b' }
		]);
	});

	it("reads a browser's bookmark export, keeping the titles", () => {
		const html = `<!DOCTYPE NETSCAPE-Bookmark-file-1>
			<DL><p>
				<DT><A HREF="https://example.com/one" ADD_DATE="1700000000">Tom &amp; Jerry</A>
				<DT><A HREF="https://example.org/two">Another   page</A>
				<DT><A HREF="place:sort=8">Most visited</A>
			</DL><p>`;

		expect(parseRows(html, 'bookmarks.html')).toEqual([
			{ url: 'https://example.com/one', note: 'Tom & Jerry' },
			{ url: 'https://example.org/two', note: 'Another page' }
		]);
	});

	it('reads a plain list of addresses and ignores the lines that are not', () => {
		const text = 'https://example.com/a\n\nnot a link\nhttps://example.com/b\n';

		expect(parseRows(text, 'links.txt')).toEqual([
			{ url: 'https://example.com/a' },
			{ url: 'https://example.com/b' }
		]);
	});

	it('finds nothing in a CSV whose header has no url column', () => {
		expect(parseRows('name,link\nSomething,https://example.com/a\n', 'wrong.csv')).toEqual([]);
	});
});
