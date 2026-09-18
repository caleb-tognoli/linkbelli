/** One snapshot of a library, as the API lists it. */
export interface Backup {
	id: string;
	takenAt: string;
	sizeBytes: number;
	playlistCount: number;
	itemCount: number;
	automatic: boolean;
}

/**
 * A file size a person can judge at a glance.
 *
 * Backups are compressed JSON, so they land between a few kilobytes and a few megabytes — the
 * range where "1.4 MB" tells you something and "1434122 bytes" does not.
 */
export function formatSize(bytes: number): string {
	if (bytes < 1024) return `${bytes} B`;
	if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;

	return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * How long ago a snapshot was taken, in the words someone would use.
 *
 * The number that matters about a backup is its age, not its date: "11 days ago" answers "can I
 * still get back what I deleted" in a way that a calendar date makes you work out.
 */
export function formatAge(takenAt: string, now: Date = new Date()): string {
	const taken = new Date(takenAt);
	if (Number.isNaN(taken.getTime())) return 'unknown';

	const minutes = Math.floor((now.getTime() - taken.getTime()) / 60_000);

	// A backup dated in the future is a clock disagreeing, not a fact about the backup. Saying
	// "just now" is closer to the truth than "in -3 minutes".
	if (minutes < 1) return 'just now';
	if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;

	const hours = Math.floor(minutes / 60);
	if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;

	const days = Math.floor(hours / 24);
	if (days < 30) return `${days} day${days === 1 ? '' : 's'} ago`;

	const months = Math.floor(days / 30);
	return `${months} month${months === 1 ? '' : 's'} ago`;
}

/** What the snapshot holds, phrased so a count of one doesn't read as a typo. */
export function describeContents(backup: Backup): string {
	const items = `${backup.itemCount} link${backup.itemCount === 1 ? '' : 's'}`;
	const playlists = `${backup.playlistCount} playlist${backup.playlistCount === 1 ? '' : 's'}`;

	return `${items} across ${playlists}`;
}

/**
 * What a restore would do, or did.
 *
 * A restore merges rather than replaces, so the interesting numbers are the two-sided ones:
 * what it would add, and what it found already there and left alone.
 */
export interface RestorePlan {
	dryRun: boolean;
	formatVersion: number;
	takenAt: string;
	foldersAdded: number;
	playlistsAdded: number;
	playlistsMatched: number;
	itemsAdded: number;
	itemsAlreadyThere: number;
	sourcesAdded: number;
	truncated: boolean;
	sourcesNeedCredentials: boolean;
	/** Marked passages put back, onto articles that are saved here once it is done. */
	highlightsAdded: number;
}
