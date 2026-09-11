import { describe, expect, it } from 'vitest';
import { average, toneColor, verdict } from './sourceHealth';
import type { SourceHealth } from './types';

function health(overrides: Partial<SourceHealth> = {}): SourceHealth {
	return {
		runs: 10,
		windowDays: 30,
		succeeded: 10,
		failed: 0,
		successRate: 100,
		averageFound: 4,
		averageAdded: 1,
		emptyRuns: 0,
		consecutiveFailures: 0,
		lastRunAt: '2026-09-01T00:00:00Z',
		lastRunStatus: 'Succeeded',
		lastError: null,
		...overrides
	};
}

describe('verdict', () => {
	it('says nothing has run rather than reporting a failure', () => {
		const result = verdict(
			health({ runs: 0, succeeded: 0, successRate: null, averageFound: null, averageAdded: null })
		);

		expect(result.tone).toBe('neutral');
		expect(result.message).toContain('30 days');
	});

	it('leads with a current failure streak', () => {
		const result = verdict(health({ consecutiveFailures: 3, failed: 3, successRate: 70 }));

		expect(result.tone).toBe('bad');
		expect(result.message).toBe('The last 3 runs have failed.');
	});

	it('counts one failure in the singular', () => {
		expect(verdict(health({ consecutiveFailures: 1, successRate: 90 })).message).toBe(
			'The last 1 run has failed.'
		);
	});

	it('flags a source that succeeds every run and finds nothing', () => {
		// The failure mode the run status can never show: 100% green, nothing coming in.
		const result = verdict(health({ emptyRuns: 10, averageFound: 0, averageAdded: 0 }));

		expect(result.tone).toBe('warn');
		expect(result.message).toContain('found nothing');
	});

	it('does not flag one quiet run among many productive ones', () => {
		expect(verdict(health({ emptyRuns: 1 })).tone).toBe('good');
	});

	it('flags a mostly-empty source without claiming every run was empty', () => {
		const result = verdict(health({ emptyRuns: 6 }));

		expect(result.tone).toBe('warn');
		expect(result.message).toBe('6 of 10 successful runs found nothing.');
	});

	it('treats a brand-new source with one empty run as quiet, not broken', () => {
		// Only one run to go on: "every run found nothing" would be true and useless.
		expect(verdict(health({ runs: 1, succeeded: 1, emptyRuns: 1 })).tone).toBe('warn');
		expect(verdict(health({ runs: 1, succeeded: 1, emptyRuns: 0 })).tone).toBe('good');
	});

	it('flags a poor success rate even with no current streak', () => {
		const result = verdict(health({ succeeded: 6, failed: 4, successRate: 60 }));

		expect(result.tone).toBe('warn');
		expect(result.message).toBe('Only 60% of recent runs succeeded.');
	});

	it('leaves an occasional failure alone', () => {
		expect(verdict(health({ succeeded: 9, failed: 1, successRate: 90 })).tone).toBe('good');
	});
});

describe('toneColor', () => {
	it('gives every tone its own variable', () => {
		const colors = (['good', 'warn', 'bad', 'neutral'] as const).map(toneColor);

		expect(new Set(colors).size).toBe(4);
	});
});

describe('average', () => {
	it('shows a dash when there is nothing to average', () => {
		expect(average(null)).toBe('—');
		expect(average(0)).toBe('0');
		expect(average(2.5)).toBe('2.5');
	});
});
