import { describe, expect, it } from 'vitest';
import { looksLikeUrl } from './urls';

describe('looksLikeUrl', () => {
	it.each([
		'https://example.com',
		'http://example.com/path?query=1',
		'HTTPS://EXAMPLE.COM',
		'  https://example.com/padded  '
	])('accepts %s', (value) => {
		expect(looksLikeUrl(value)).toBe(true);
	});

	it.each([
		'example.com',
		'ftp://example.com',
		'just some text',
		'',
		'https://'
	])('rejects %s', (value) => {
		expect(looksLikeUrl(value)).toBe(false);
	});

	it('does not treat a URL mentioned mid-sentence as a URL', () => {
		expect(looksLikeUrl('see https://example.com for details')).toBe(false);
	});

	it('accepts an empty authority, which the URL standard collapses into a host', () => {
		// http:///path parses as http://path/ — surprising, but it is a real address, so
		// treating it as one is right.
		expect(looksLikeUrl('http:///path')).toBe(true);
	});
});
