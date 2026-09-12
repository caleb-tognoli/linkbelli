import { describe, expect, it } from 'vitest';
import { isPreviewable, previewKey } from './sourcePreview';

describe('isPreviewable', () => {
	it('waits until the feed URL is actually an address', () => {
		// Asking the server to fetch "https://ex" spends a rate-limited request on nothing.
		expect(isPreviewable('Rss', { feedUrl: 'https://ex' })).toBe(false);
		expect(isPreviewable('Rss', { feedUrl: 'https://example.com/feed.xml' })).toBe(true);
	});

	it('needs every required field, not just one', () => {
		expect(isPreviewable('Scraper', { url: 'https://example.com' })).toBe(false);
		expect(isPreviewable('Scraper', { url: 'https://example.com', itemSelector: 'a.item' })).toBe(true);
	});

	it('ignores whitespace-only values', () => {
		expect(isPreviewable('Rss', { feedUrl: '   ' })).toBe(false);
	});

	it('never previews a webhook, which has nothing to fetch', () => {
		expect(isPreviewable('Webhook', { token: 'abc' })).toBe(false);
	});

	it('is happy with a JSON API once it knows where the items are', () => {
		expect(isPreviewable('JsonApi', { url: 'https://api.example.com/v1/things' })).toBe(false);
		expect(
			isPreviewable('JsonApi', { url: 'https://api.example.com/v1/things', itemsPath: '$.results' })
		).toBe(true);
	});
});

describe('previewKey', () => {
	it('is the same for the same config however it was typed', () => {
		const a = previewKey('Scraper', { url: 'https://example.com', itemSelector: 'a' });
		const b = previewKey('Scraper', { itemSelector: 'a', url: 'https://example.com' });

		// Focusing and blurring a field must not spend another rate-limited fetch.
		expect(a).toBe(b);
	});

	it('ignores trailing whitespace and empty fields', () => {
		const a = previewKey('Rss', { feedUrl: 'https://example.com/feed' });
		const b = previewKey('Rss', { feedUrl: 'https://example.com/feed ', linkAttribute: '' });

		expect(a).toBe(b);
	});

	it('changes when the config does', () => {
		const a = previewKey('Rss', { feedUrl: 'https://example.com/one' });
		const b = previewKey('Rss', { feedUrl: 'https://example.com/two' });

		expect(a).not.toBe(b);
	});

	it('changes when the type does', () => {
		expect(previewKey('Rss', { url: 'https://example.com' })).not.toBe(
			previewKey('Scraper', { url: 'https://example.com' })
		);
	});
});
