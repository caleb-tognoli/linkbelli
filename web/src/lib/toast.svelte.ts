/**
 * One place for "this happened" and "that did not work".
 *
 * Every screen used to invent its own: an inline paragraph above the link table that never went
 * away and sat off-screen once you had scrolled, plain text under the source list with no role,
 * a fixed bar in the reader, another in the drop tray with a lowercase "dismiss", banners on the
 * rules and tags pages. A message now goes here and is drawn once, by <Toaster /> in the root
 * layout: at the bottom of the screen wherever you are, announced to screen readers, gone by
 * itself when it is good news and kept until dismissed when it is not.
 */

export type ToastTone = 'info' | 'success' | 'error';

export interface ToastAction {
	label: string;
	run: () => void | Promise<void>;
}

export interface Toast {
	id: number;
	text: string;
	tone: ToastTone;
	action?: ToastAction;
	/** Milliseconds before it goes by itself, or null to stay until dismissed. */
	duration: number | null;
}

interface Options {
	action?: ToastAction;
	duration?: number | null;
}

/** Long enough to read a sentence and reach an Undo button. */
const DEFAULT_DURATION = 5000;
/** More than this and the oldest goes; a stack of stale messages helps nobody. */
const MAX_VISIBLE = 4;

let list = $state<Toast[]>([]);
let nextId = 1;

function push(tone: ToastTone, text: string, options: Options = {}): number {
	const id = nextId++;
	// Errors stay: somebody who looked away should still find out a change did not happen.
	const duration = options.duration !== undefined ? options.duration : tone === 'error' ? null : DEFAULT_DURATION;
	list = [...list, { id, text, tone, action: options.action, duration }].slice(-MAX_VISIBLE);
	return id;
}

export const toast = {
	get list(): Toast[] {
		return list;
	},
	info: (text: string, options?: Options) => push('info', text, options),
	success: (text: string, options?: Options) => push('success', text, options),
	error: (text: string, options?: Options) => push('error', text, options),
	dismiss(id: number) {
		list = list.filter((t) => t.id !== id);
	},
	/** For tests: start from nothing. */
	clear() {
		list = [];
	}
};
