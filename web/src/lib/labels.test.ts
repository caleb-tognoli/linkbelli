import { describe, expect, it } from 'vitest';
import { displayUsername } from './labels';

describe('displayUsername', () => {
	it('puts an @ in front of an ordinary name', () => {
		expect(displayUsername('caleb')).toBe('@caleb');
	});

	it('keeps only the local part of a name that is an address', () => {
		// Older accounts predate the rule against this, and the whole thing used to be printed
		// on a public page.
		expect(displayUsername('someone@example.com')).toBe('@someone');
	});

	it('leaves a name that merely starts with an @ alone', () => {
		expect(displayUsername('@handle')).toBe('@@handle');
	});

	it('has something to say about nobody', () => {
		expect(displayUsername(null)).toBe('@someone');
		expect(displayUsername(undefined)).toBe('@someone');
		expect(displayUsername('')).toBe('@someone');
	});
});
