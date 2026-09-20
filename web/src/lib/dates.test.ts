import { describe, expect, it } from 'vitest';
import { formatDate } from './dates';

/**
 * Dates across the app said "Mar 3" with no year, so something saved two years ago read like
 * something saved last week.
 */
describe('formatDate', () => {
	const now = new Date('2026-09-20T12:00:00Z').getTime();

	it('says recent things in days', () => {
		expect(formatDate('2026-09-20T09:00:00Z', now)).toBe('today');
		expect(formatDate('2026-09-19T09:00:00Z', now)).toBe('yesterday');
		expect(formatDate('2026-09-17T09:00:00Z', now)).toBe('3d ago');
	});

	it('drops the year within this year and keeps it before', () => {
		// The order of day and month is the reader's locale's business; the year is ours.
		expect(formatDate('2026-03-03T09:00:00Z', now)).not.toMatch(/\d{4}/);
		expect(formatDate('2024-03-03T09:00:00Z', now)).toMatch(/2024/);
	});

	it('has something to say about nothing', () => {
		expect(formatDate(null, now)).toBe('—');
		expect(formatDate('not a date', now)).toBe('—');
	});
});
