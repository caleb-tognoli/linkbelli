/**
 * The two overlays anything on the page may need to open.
 *
 * The command palette answered Ctrl+K and "/" and nothing else — no button anywhere, so on a
 * phone there was no way to reach it at all — and the keyboard shortcuts were listed in a
 * footnote under one table, hidden below `sm`. Both are opened from here now, so a button in the
 * sidebar, a row of keys in a footer, and the key handlers can all mean the same thing.
 */
class Overlay {
	open = $state(false);

	show() {
		this.open = true;
	}

	toggle() {
		this.open = !this.open;
	}
}

/** Search-or-jump. */
export const palette = new Overlay();

/** The list of keys, and what they do. */
export const shortcuts = new Overlay();
