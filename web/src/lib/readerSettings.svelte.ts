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

/** A year: long enough that the setting outlives any reasonable gap between readings. */
const COOKIE_MAX_AGE = 31_536_000;

export interface Stored {
	size: ReaderSize;
	width: ReaderWidth;
	font: ReaderFont;
}

const DEFAULTS: Stored = { size: 'medium', width: 'narrow', font: 'sans' };

/**
 * Reads a stored value, whatever it came from.
 *
 * Shared with the server, which reads the same shape out of the cookie — a value that is not one
 * of the names this version knows is ignored rather than trusted.
 */
export function parseSettings(raw: string | null | undefined): Partial<Stored> {
	if (!raw) return {};

	try {
		const stored = JSON.parse(raw) as Partial<Stored>;
		return {
			...(stored.size && SIZES.includes(stored.size) ? { size: stored.size } : {}),
			...(stored.width && WIDTHS.includes(stored.width) ? { width: stored.width } : {}),
			...(stored.font && FONTS.includes(stored.font) ? { font: stored.font } : {})
		};
	} catch {
		// Blocked site data, a private window, or something else's key at this name. The
		// defaults are a perfectly good place to read from.
		return {};
	}
}

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

	/**
	 * Applies what the server read out of the cookie, before the first paint.
	 *
	 * These used to be read from localStorage in onMount, so an article opened at "large, serif"
	 * was drawn once at the defaults and then re-drawn — the page jumped under the reader's eyes
	 * every single time.
	 */
	hydrate(stored: Partial<Stored> | null | undefined) {
		if (!stored) return;
		if (stored.size) this.size = stored.size;
		if (stored.width) this.width = stored.width;
		if (stored.font) this.font = stored.font;
	}

	/**
	 * Reads what this device stored, for anything the server did not send.
	 *
	 * Still reads localStorage: settings kept before the cookie existed are moved across on the
	 * next read rather than quietly reverting to the defaults.
	 */
	load() {
		if (!browser) return;

		try {
			const stored = parseSettings(localStorage.getItem(KEY));
			if (Object.keys(stored).length === 0) return;
			this.hydrate(stored);
			this.save();
		} catch {
			// Nowhere to read from. The defaults will do.
		}
	}

	set<K extends keyof Stored>(key: K, value: Stored[K]) {
		this[key] = value as never;
		this.save();
	}

	private save() {
		if (!browser) return;

		const value = JSON.stringify({ size: this.size, width: this.width, font: this.font });

		// In a cookie as well as in localStorage, so the server can draw the article at the right
		// size the first time rather than leaving the browser to correct it.
		try {
			document.cookie = `${KEY}=${encodeURIComponent(value)}; path=/; max-age=${COOKIE_MAX_AGE}; samesite=lax`;
		} catch {
			// Cookies refused. localStorage may still work.
		}

		try {
			localStorage.setItem(KEY, value);
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
