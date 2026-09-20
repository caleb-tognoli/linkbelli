/**
 * A filter chip: the small outlined pill a list wears to say how it has been narrowed.
 *
 * The link table had its own version of this and the search page had another — bordered buttons
 * at the full control height, with a native select dropped in among them — so the two places you
 * narrow a list of links looked like two different products. One shape, in one place, for both.
 *
 * Accent when the chip is doing something, muted when it is not, so a glance at the row says what
 * is on. A chip that narrows *away* from what you want (Broken) says so in the danger colour
 * instead; pass it as `tone`.
 */
export type ChipTone = 'accent' | 'danger';

const BASE =
	'inline-flex min-h-6 items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs transition-colors';

export function filterChipClass(active: boolean, tone: ChipTone = 'accent', extra = ''): string {
	const on = tone === 'danger' ? 'border-danger text-danger' : 'border-accent text-accent';

	return [BASE, active ? on : 'border-border text-muted', extra].filter(Boolean).join(' ');
}
