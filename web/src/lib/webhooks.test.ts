import { describe, expect, it } from 'vitest';
import { canRedeliver, describeDelivery, shortUrl, type WebhookDelivery } from './webhooks';

function delivery(over: Partial<WebhookDelivery> = {}): WebhookDelivery {
	return {
		id: 'd1',
		event: 'items.added',
		status: 'Delivered',
		attempts: 1,
		responseStatus: 200,
		error: null,
		createdAt: '2026-09-18T00:00:00Z',
		lastAttemptAt: null,
		nextAttemptAt: null,
		deliveredAt: null,
		...over
	};
}

describe('shortUrl', () => {
	it('leaves a short address alone', () => {
		expect(shortUrl('https://hooks.example/abc')).toBe('https://hooks.example/abc');
	});

	/** The end of the path is usually the token that tells two automations apart. */
	it('keeps the host and both ends of a long path', () => {
		const url = 'http://192.168.1.10:8123/api/webhook/linkbelli-new-links-to-kitchen-display-abc123';
		const short = shortUrl(url, 60);

		expect(short.length).toBe(60);
		expect(short.startsWith('http://192.168.1.10:8123/api/')).toBe(true);
		expect(short.endsWith('abc123')).toBe(true);
		expect(short).toContain('…');
	});

	it('copes with something that is not an address', () => {
		expect(shortUrl('x'.repeat(80), 20)).toBe(`${'x'.repeat(19)}…`);
	});
});

describe('describeDelivery', () => {
	it('says plainly when it arrived', () => {
		expect(describeDelivery(delivery())).toBe('Delivered');
		expect(describeDelivery(delivery({ attempts: 3 }))).toBe('Delivered after 3 tries');
	});

	it('says it is still trying, and how often it has failed', () => {
		expect(describeDelivery(delivery({ status: 'Retrying', attempts: 1 }))).toBe(
			'Failed once, trying again'
		);
		expect(describeDelivery(delivery({ status: 'Retrying', attempts: 2 }))).toBe(
			'Failed 2 times, trying again'
		);
	});

	it('says when it gave up', () => {
		expect(describeDelivery(delivery({ status: 'Failed', attempts: 5 }))).toBe(
			'Gave up after 5 tries'
		);
	});
});

describe('canRedeliver', () => {
	it('offers it for anything that has finished, either way', () => {
		expect(canRedeliver(delivery({ status: 'Failed' }))).toBe(true);
		expect(canRedeliver(delivery({ status: 'Delivered' }))).toBe(true);
		expect(canRedeliver(delivery({ status: 'Cancelled' }))).toBe(true);
	});

	it('does not offer it while a delivery is still in progress', () => {
		expect(canRedeliver(delivery({ status: 'Retrying' }))).toBe(false);
		expect(canRedeliver(delivery({ status: 'Pending' }))).toBe(false);
	});
});
