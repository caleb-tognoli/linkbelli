import { browser } from '$app/environment';

/** How big the text is. Named rather than numbered, because that is how people choose. */
export const SIZES = ['small', 'medium', 'large', 'larger'] as const;
export type ReaderSize = (typeof SIZES)[number];

/** How wide the column is. "Narrow" is a book; "wide" is what a big screen can take. */
export const WIDTHS = ['narrow', 'medium', 'wide'] as const;
export type ReaderWidth = (typeof WIDTHS)[number];

export const FONTS = ['sans', 'serif'] as const;
export type ReaderFont = (typeof FONTS)[number];

const KEY = 'lb_reader';

interface Stored {
	size: ReaderSize;
	width: ReaderWidth;
	font: ReaderFont;
}

const DEFAULTS: Stored = { size: 'medium', width: 'narrow', font: 'sans' };

/**
 * How somebody likes to read.
 *
 * Per device, in localStorage, and deliberately not on the account: this is a property of the
 * screen you are looking at, not of you. The right size on a phone is the wrong one on a desktop,
 * and syncing it would mean fighting the setting every time you switched.
 */
class ReaderSettings {
	size = $state<ReaderSize>(DEFAULTS.size);
	width = $state<ReaderWidth>(DEFAULTS.width);
	font = $state<ReaderFont>(DEFAULTS.font);

	/** Reads what was stored. Safe to call when there is nothing, or nowhere to read from. */
	load() {
		if (!browser) return;

		try {
			const raw = localStorage.getItem(KEY);
			if (!raw) return;

			const stored = JSON.parse(raw) as Partial<Stored>;
			if (stored.size && SIZES.includes(stored.size)) this.size = stored.size;
			if (stored.width && WIDTHS.includes(stored.width)) this.width = stored.width;
			if (stored.font && FONTS.includes(stored.font)) this.font = stored.font;
		} catch {
			// Blocked site data, a private window, or something else's key at this name. The
			// defaults are a perfectly good place to read from.
		}
	}

	set<K extends keyof Stored>(key: K, value: Stored[K]) {
		this[key] = value as never;
		this.save();
	}

	private save() {
		if (!browser) return;

		try {
			localStorage.setItem(
				KEY,
				JSON.stringify({ size: this.size, width: this.width, font: this.font })
			);
		} catch {
			// Nowhere to keep it. The setting still applies to this page, which is most of it.
		}
	}
}

export const readerSettings = new ReaderSettings();

/** Tailwind-free, because these are read off a store rather than written in a class list. */
export const SIZE_CSS: Record<ReaderSize, string> = {
	small: '0.95rem',
	medium: '1.05rem',
	large: '1.2rem',
	larger: '1.4rem'
};

export const WIDTH_CSS: Record<ReaderWidth, string> = {
	narrow: '38rem',
	medium: '48rem',
	wide: '62rem'
};

export const FONT_CSS: Record<ReaderFont, string> = {
	sans: 'inherit',
	serif: 'Georgia, "Iowan Old Style", "Times New Roman", serif'
};
