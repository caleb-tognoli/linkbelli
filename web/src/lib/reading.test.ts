import { describe, expect, it } from 'vitest';
import { readingLabel, readingMinutes, WORDS_PER_MINUTE } from './reading';

describe('readingMinutes', () => {
	it('has nothing to say about a page with no article in it', () => {
		expect(readingMinutes(null)).toBeNull();
		expect(readingMinutes(undefined)).toBeNull();
		expect(readingMinutes(0)).toBeNull();
	});

	it('never reports zero minutes', () => {
		// "0 min read" reads as a bug rather than as "very short".
		expect(readingMinutes(5)).toBe(1);
		expect(readingMinutes(100)).toBe(1);
	});

	it('rounds to the nearest minute', () => {
		expect(readingMinutes(WORDS_PER_MINUTE * 4)).toBe(4);
		expect(readingMinutes(WORDS_PER_MINUTE * 4 + WORDS_PER_MINUTE / 2 + 1)).toBe(5);
	});

	it('handles a long read without exaggerating it', () => {
		expect(readingMinutes(12_000)).toBe(55);
	});
});

describe('readingLabel', () => {
	it('labels what it can and stays quiet otherwise', () => {
		expect(readingLabel(660)).toBe('3 min');
		expect(readingLabel(null)).toBeNull();
	});
});
