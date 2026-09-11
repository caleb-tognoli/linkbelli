import { describe, expect, it } from 'vitest';
import { extractUrl, sharedUrl } from './share';

const params = (query: string) => new URLSearchParams(query);

describe('sharedUrl', () => {
	it('takes an explicit url, which is what a bookmarklet sends', () => {
		expect(sharedUrl(params('url=https://example.com/a&title=A+page'))).toBe('https://example.com/a');
	});

	it('falls back to the text field, which is where Android often puts it', () => {
		expect(sharedUrl(params('text=https://example.com/a'))).toBe('https://example.com/a');
	});

	it('prefers the url field when both are present', () => {
		expect(sharedUrl(params('url=https://right.example/&text=https://wrong.example/'))).toBe(
			'https://right.example/'
		);
	});

	it('is empty when nothing was shared', () => {
		expect(sharedUrl(params(''))).toBe('');
		expect(sharedUrl(params('title=Just+a+title'))).toBe('');
	});
});

describe('extractUrl', () => {
	it('fishes an address out of a sentence', () => {
		// Sharing from an app usually appends the link to the person's own words.
		expect(extractUrl('Look at this https://example.com/a it is good')).toBe(
			'https://example.com/a'
		);
	});

	it('takes the first address when there are several', () => {
		expect(extractUrl('https://first.example/ and https://second.example/')).toBe(
			'https://first.example/'
		);
	});

	it('handles http as well as https', () => {
		expect(extractUrl('see http://example.com/a')).toBe('http://example.com/a');
	});

	it('stops at whitespace rather than swallowing the rest', () => {
		expect(extractUrl('https://example.com/a more words')).toBe('https://example.com/a');
	});

	it.each([null, '', 'no address here', 'example.com without a scheme'])(
		'returns null for %s',
		(text) => {
			expect(extractUrl(text)).toBeNull();
		}
	);
});
