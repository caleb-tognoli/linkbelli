import { describe, expect, it } from 'vitest';
import { isPlainKey, isTypingTarget, moveFocus } from './keyboard';

function element(tag: string, contentEditable = false): HTMLElement {
	const node = document.createElement(tag);
	// setAttribute rather than the property: jsdom does not derive isContentEditable from the
	// property assignment, which is the very gap the implementation guards against.
	if (contentEditable) node.setAttribute('contenteditable', 'true');
	return node;
}

function keyEvent(key: string, options: Partial<KeyboardEvent> = {}, target?: EventTarget) {
	const event = new KeyboardEvent('keydown', { key, ...options });
	if (target) Object.defineProperty(event, 'target', { value: target });
	return event;
}

describe('isTypingTarget', () => {
	it.each(['input', 'textarea', 'select'])('treats %s as being typed into', (tag) => {
		expect(isTypingTarget(element(tag))).toBe(true);
	});

	it('treats a contenteditable element as being typed into', () => {
		expect(isTypingTarget(element('div', true))).toBe(true);
	});

	it.each(['div', 'button', 'a', 'body'])('treats %s as not being typed into', (tag) => {
		expect(isTypingTarget(element(tag))).toBe(false);
	});

	it('copes with no target at all', () => {
		expect(isTypingTarget(null)).toBe(false);
	});
});

describe('isPlainKey', () => {
	it('accepts a bare keystroke on the page', () => {
		expect(isPlainKey(keyEvent('j', {}, element('div')))).toBe(true);
	});

	it.each([
		['ctrlKey', { ctrlKey: true }],
		['metaKey', { metaKey: true }],
		['altKey', { altKey: true }]
	])('refuses a keystroke held with %s, which the browser may claim', (_label, modifier) => {
		expect(isPlainKey(keyEvent('j', modifier, element('div')))).toBe(false);
	});

	it('refuses anything typed into a field', () => {
		// Otherwise writing "join the queue" in a note would scroll the list under it.
		expect(isPlainKey(keyEvent('j', {}, element('textarea')))).toBe(false);
	});

	it('allows shift, which is part of ordinary typing of a letter', () => {
		expect(isPlainKey(keyEvent('J', { shiftKey: true }, element('div')))).toBe(true);
	});
});

describe('moveFocus', () => {
	it('moves down and up within the list', () => {
		expect(moveFocus(0, 1, 5)).toBe(1);
		expect(moveFocus(3, -1, 5)).toBe(2);
	});

	it('lands on the first row from nothing selected', () => {
		expect(moveFocus(-1, 1, 5)).toBe(0);
	});

	it('stops at the ends rather than wrapping', () => {
		// Wrapping from the bottom to the top loses your place in a long list.
		expect(moveFocus(4, 1, 5)).toBe(4);
		expect(moveFocus(0, -1, 5)).toBe(0);
	});

	it('has nowhere to go in an empty list', () => {
		expect(moveFocus(-1, 1, 0)).toBe(-1);
		expect(moveFocus(0, -1, 0)).toBe(-1);
	});

	it('clamps a selection that is past the end after the list shrank', () => {
		expect(moveFocus(9, 1, 3)).toBe(2);
	});
});
