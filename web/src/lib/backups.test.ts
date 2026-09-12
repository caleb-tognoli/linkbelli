import { describe, expect, it } from 'vitest';
import { describeContents, formatAge, formatSize, type Backup } from './backups';

const backup = (over: Partial<Backup> = {}): Backup => ({
	id: 'b1',
	takenAt: '2026-09-01T00:00:00Z',
	sizeBytes: 1024,
	playlistCount: 2,
	itemCount: 10,
	automatic: true,
	...over
});

describe('formatSize', () => {
	it('keeps small snapshots in bytes', () => {
		expect(formatSize(0)).toBe('0 B');
		expect(formatSize(900)).toBe('900 B');
	});

	it('switches to kilobytes and then megabytes', () => {
		expect(formatSize(2048)).toBe('2 KB');
		expect(formatSize(1_500_000)).toBe('1.4 MB');
	});
});

describe('formatAge', () => {
	const now = new Date('2026-09-12T12:00:00Z');

	it('says how long ago, not when', () => {
		expect(formatAge('2026-09-12T11:59:30Z', now)).toBe('just now');
		expect(formatAge('2026-09-12T11:30:00Z', now)).toBe('30 minutes ago');
		expect(formatAge('2026-09-12T09:00:00Z', now)).toBe('3 hours ago');
		expect(formatAge('2026-09-01T12:00:00Z', now)).toBe('11 days ago');
		expect(formatAge('2026-07-01T12:00:00Z', now)).toBe('2 months ago');
	});

	it('does not say "1 minutes"', () => {
		expect(formatAge('2026-09-12T11:59:00Z', now)).toBe('1 minute ago');
		expect(formatAge('2026-09-12T11:00:00Z', now)).toBe('1 hour ago');
		expect(formatAge('2026-09-11T12:00:00Z', now)).toBe('1 day ago');
	});

	it('treats a snapshot from the future as just now', () => {
		// Clocks disagree; "in -3 minutes" tells the reader nothing true about their backup.
		expect(formatAge('2026-09-12T12:05:00Z', now)).toBe('just now');
	});

	it('does not pretend to know when the date is unreadable', () => {
		expect(formatAge('not a date', now)).toBe('unknown');
	});
});

describe('describeContents', () => {
	it('counts what is in it', () => {
		expect(describeContents(backup())).toBe('10 links across 2 playlists');
	});

	it('does not say "1 links"', () => {
		expect(describeContents(backup({ itemCount: 1, playlistCount: 1 }))).toBe(
			'1 link across 1 playlist'
		);
	});

	it('says plainly that a snapshot is empty', () => {
		expect(describeContents(backup({ itemCount: 0, playlistCount: 0 }))).toBe(
			'0 links across 0 playlists'
		);
	});
});
