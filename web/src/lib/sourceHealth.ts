import type { SourceHealth } from '$lib/types';

export type Tone = 'good' | 'warn' | 'bad' | 'neutral';

export interface Verdict {
	tone: Tone;
	/** One line, in the owner's terms: what is wrong, not which counter is high. */
	message: string;
}

/** Below this, a source is failing often enough to be worth saying so. */
const POOR_RATE = 80;

/** Consecutive empty successes that mean "this has stopped finding things", not "quiet week". */
const EMPTY_STREAK = 3;

/**
 * The one sentence worth putting above the numbers.
 *
 * The counters alone can't answer the question an owner actually has, because the worst failure
 * mode looks perfect in them: a scraper whose selector stopped matching succeeds every single
 * run and returns an empty list forever.
 */
export function verdict(health: SourceHealth): Verdict {
	if (health.runs === 0) {
		return { tone: 'neutral', message: 'No runs in the last ' + health.windowDays + ' days.' };
	}

	if (health.consecutiveFailures > 0) {
		const runs = health.consecutiveFailures === 1 ? 'run has' : 'runs have';
		return {
			tone: 'bad',
			message: `The last ${health.consecutiveFailures} ${runs} failed.`
		};
	}

	// Succeeding and finding nothing, repeatedly. Checked before the success rate, because this
	// case has a perfect one.
	if (health.succeeded > 0 && health.emptyRuns >= Math.min(EMPTY_STREAK, health.succeeded)) {
		if (health.emptyRuns === health.succeeded) {
			return {
				tone: 'warn',
				message: 'Every run succeeded and found nothing — the feed may have moved or the selector may no longer match.'
			};
		}

		return {
			tone: 'warn',
			message: `${health.emptyRuns} of ${health.succeeded} successful runs found nothing.`
		};
	}

	if (health.successRate !== null && health.successRate < POOR_RATE) {
		return { tone: 'warn', message: `Only ${health.successRate}% of recent runs succeeded.` };
	}

	return { tone: 'good', message: 'Running normally.' };
}

/** The CSS colour variable a tone paints with. */
export function toneColor(tone: Tone): string {
	switch (tone) {
		case 'good':
			return 'var(--color-success)';
		case 'warn':
			return 'var(--color-warning)';
		case 'bad':
			return 'var(--color-danger)';
		default:
			return 'var(--color-muted)';
	}
}

/** "2.4 a run", or a dash when nothing has succeeded yet and there is no average to give. */
export function average(value: number | null): string {
	return value === null ? '—' : String(value);
}
