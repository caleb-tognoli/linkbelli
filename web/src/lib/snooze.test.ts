import { describe, expect, it } from 'vitest';
import { backWhen, resolvePreset } from './snooze';

/**
 * When "not now" means.
 *
 * Presets rather than a date picker: "not now" is a feeling, not a date, and being made to choose
 * a Tuesday to express it is why snooze buttons go unused. Which means the five have to land
 * where somebody would expect, or they are worse than the picker.
 *
 * Resolved in the browser because the browser is the only thing that knows the reader's timezone.
 * The API has the same five and uses the server's clock, which is the caller's evening only by
 * coincidence.
 */
describe('resolvePreset', () => {
	// A Wednesday.
	const wednesdayNoon = new Date(2026, 2, 4, 12, 0, 0);

	it('sends tonight to this evening while it is still the day', () => {
		const at = resolvePreset('tonight', wednesdayNoon);

		expect(at.getDate()).toBe(4);
		expect(at.getHours()).toBe(19);
	});

	it('sends tonight to tomorrow evening once the evening has been and gone', () => {
		const at = resolvePreset('tonight', new Date(2026, 2, 4, 22, 0, 0));

		expect(at.getDate()).toBe(5);
		expect(at.getHours()).toBe(19);
	});

	it('sends the weekend to the next Saturday morning', () => {
		const at = resolvePreset('weekend', wednesdayNoon);

		expect(at.getDay()).toBe(6);
		expect(at.getDate()).toBe(7);
		expect(at.getHours()).toBe(8);
	});

	/** Snoozing on a Saturday is not asking for five minutes. */
	it('sends the weekend to the one after when it already is the weekend', () => {
		const saturday = new Date(2026, 2, 7, 10, 0, 0);
		expect(saturday.getDay()).toBe(6);

		expect(resolvePreset('weekend', saturday).getDate()).toBe(14);
	});

	it('always lands in the future, from any hour', () => {
		for (const hour of [0, 8, 12, 19, 23]) {
			const now = new Date(2026, 2, 4, hour, 30, 0);

			for (const preset of ['tonight', 'tomorrow', 'weekend', 'week', 'month'] as const) {
				expect(resolvePreset(preset, now).getTime()).toBeGreaterThan(now.getTime());
			}
		}
	});
});

/** Never a raw timestamp: what people want to know is roughly when it comes back. */
describe('backWhen', () => {
	const now = new Date(2026, 2, 4, 12, 0, 0);

	it('says tonight, tomorrow, a weekday, or a date', () => {
		expect(backWhen(new Date(2026, 2, 4, 19, 0, 0).toISOString(), now)).toBe('back tonight');
		expect(backWhen(new Date(2026, 2, 5, 8, 0, 0).toISOString(), now)).toBe('back tomorrow');
		expect(backWhen(new Date(2026, 2, 7, 8, 0, 0).toISOString(), now)).toContain('Saturday');
		expect(backWhen(new Date(2026, 3, 4, 8, 0, 0).toISOString(), now)).toMatch(/back on \d/);
	});

	it('says so when the moment has already passed', () => {
		expect(backWhen(new Date(2026, 2, 3).toISOString(), now)).toBe('back now');
	});
});
