/**
 * How a date is written in the app.
 *
 * Rows showed "Mar 3" with no year, so a link saved in 2024 read as one saved last week, and run
 * histories printed `toLocaleString()` in full — "3/3/2026, 09:41:12" — beside columns of counts.
 * One helper, three shapes: what happened in the last week is said in days, this year's dates
 * drop the year, and older ones keep it.
 */

const DAY = 86_400_000;

/** Under this many days, a date is easier to read as an age than as a date. */
const RELATIVE_DAYS = 7;

/**
 * A date, in the shortest form that is still unambiguous.
 *
 * `now` is a parameter so a test does not have to travel in time.
 */
export function formatDate(iso: string | null | undefined, now: number = Date.now()): string {
	if (!iso) return '—';

	const then = new Date(iso);
	if (Number.isNaN(then.getTime())) return '—';

	const days = Math.floor((now - then.getTime()) / DAY);

	if (days < 0) {
		// In the future: a snooze, or a clock that disagrees. Say the date rather than "-2d ago".
		return withYear(then, now);
	}

	if (days === 0) return 'today';
	if (days === 1) return 'yesterday';
	if (days < RELATIVE_DAYS) return `${days}d ago`;

	return withYear(then, now);
}

/** The full timestamp, for a `title` or a `<time>` — where the exact moment is worth having. */
export function formatExact(iso: string | null | undefined): string {
	if (!iso) return '';

	const then = new Date(iso);
	return Number.isNaN(then.getTime()) ? '' : then.toLocaleString();
}

/** "Mar 3" within this year, "Mar 3, 2025" before it. */
function withYear(date: Date, now: number): string {
	const sameYear = date.getFullYear() === new Date(now).getFullYear();

	return date.toLocaleDateString(undefined, {
		month: 'short',
		day: 'numeric',
		...(sameYear ? {} : { year: 'numeric' })
	});
}
