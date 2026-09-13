/**
 * When "not now" means, worked out on the reader's own clock.
 *
 * The API has the same five presets, and uses the server's clock for them — which is the caller's
 * evening only by coincidence. The browser knows the timezone, so it resolves them here and sends
 * an explicit moment; the server's version is the fallback for a script or an assistant.
 */
export const PRESETS = ['tonight', 'tomorrow', 'weekend', 'week', 'month'] as const;
export type SnoozePreset = (typeof PRESETS)[number];

/** Matches SnoozePresets on the API side. */
const EVENING_HOUR = 19;
const MORNING_HOUR = 8;

export const PRESET_LABELS: Record<SnoozePreset, string> = {
	tonight: 'Tonight',
	tomorrow: 'Tomorrow',
	weekend: 'The weekend',
	week: 'Next week',
	month: 'Next month'
};

function at(day: Date, hour: number): Date {
	const result = new Date(day);
	result.setHours(hour, 0, 0, 0);
	return result;
}

function addDays(from: Date, days: number): Date {
	const result = new Date(from);
	result.setDate(result.getDate() + days);
	return result;
}

export function resolvePreset(preset: SnoozePreset, now = new Date()): Date {
	switch (preset) {
		case 'tonight':
			// Already evening: they mean tomorrow evening, not four minutes from now.
			return now.getHours() < EVENING_HOUR
				? at(now, EVENING_HOUR)
				: at(addDays(now, 1), EVENING_HOUR);
		case 'tomorrow':
			return at(addDays(now, 1), MORNING_HOUR);
		case 'weekend': {
			// The next Saturday. On a Saturday it means the one after — somebody snoozing on a
			// Saturday is not asking for five minutes.
			const days = (6 - now.getDay() + 7) % 7;
			return at(addDays(now, days === 0 ? 7 : days), MORNING_HOUR);
		}
		case 'week':
			return at(addDays(now, 7), MORNING_HOUR);
		case 'month': {
			const result = new Date(now);
			result.setMonth(result.getMonth() + 1);
			return at(result, MORNING_HOUR);
		}
	}
}

/** "back tonight", "back on Saturday", "back on 4 April" — never a raw timestamp. */
export function backWhen(iso: string, now = new Date()): string {
	const due = new Date(iso);
	const days = Math.round((due.getTime() - now.getTime()) / 86_400_000);

	if (due <= now) return 'back now';
	if (days < 1) return due.getHours() >= 17 ? 'back tonight' : 'back later today';
	if (days === 1) return 'back tomorrow';
	if (days < 7) return `back on ${due.toLocaleDateString(undefined, { weekday: 'long' })}`;

	return `back on ${due.toLocaleDateString(undefined, { day: 'numeric', month: 'long' })}`;
}
