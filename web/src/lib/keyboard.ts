/**
 * Whether a keystroke was meant for the page rather than for something being typed into.
 *
 * Single-letter shortcuts are only safe if they never fire while someone is writing a note, a
 * search term or a name — which is most of what this app is made of.
 */
export function isTypingTarget(target: EventTarget | null): boolean {
	if (!(target instanceof HTMLElement)) return false;

	const tag = target.tagName;
	if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;

	// Both the derived property and the attribute: the property is the browser's answer, but it
	// is not implemented everywhere, and an undefined would make this question answer neither
	// yes nor no.
	return target.isContentEditable === true || target.getAttribute('contenteditable') === 'true';
}

/**
 * Whether a keystroke was meant for the control that has the keyboard.
 *
 * A button, a link or a menu item answers Enter and Space itself, and a page-level shortcut that
 * takes those keys takes them away from it: with a row picked out by j, Enter on the focused
 * "Paste links" button called preventDefault and opened the row's link in a new tab instead of
 * opening the dialog. Broader than isTypingTarget, which only asks whether something is being
 * written into.
 */
export function isInteractiveTarget(target: EventTarget | null): boolean {
	if (!(target instanceof HTMLElement)) return false;
	if (isTypingTarget(target)) return true;

	return !!target.closest(
		'button, a[href], summary, [role="button"], [role="link"], [role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"], [role="option"], [role="tab"], [role="radio"], [role="switch"], [role="checkbox"]'
	);
}

/**
 * True when a keystroke should be treated as a bare shortcut. A modifier means the browser or the
 * operating system has its own claim on it.
 */
export function isPlainKey(event: KeyboardEvent): boolean {
	return !event.ctrlKey && !event.metaKey && !event.altKey && !isTypingTarget(event.target);
}

/**
 * The index to move to, clamped to the list. Returns -1 for an empty list, and treats "no
 * selection yet" as starting before the first row so the first press lands on it.
 */
export function moveFocus(current: number, delta: number, count: number): number {
	if (count === 0) return -1;

	const next = current < 0 && delta > 0 ? 0 : current + delta;
	return Math.min(count - 1, Math.max(0, next));
}
