/**
 * Words a minute for ordinary prose. Adult reading speeds spread from roughly 200 to 300, so
 * this sits deliberately low: a five-minute estimate that turns out to take four is a pleasant
 * surprise, and one that takes eight is a broken promise.
 */
export const WORDS_PER_MINUTE = 220;

/**
 * How long an article takes to read, in whole minutes. Null when there is nothing to estimate
 * from, and never zero — "0 min read" reads as an error, not as "very short".
 */
export function readingMinutes(wordCount: number | null | undefined): number | null {
	if (!wordCount || wordCount <= 0) return null;

	return Math.max(1, Math.round(wordCount / WORDS_PER_MINUTE));
}

/** The same figure as a label, for a row that has no space to explain itself. */
export function readingLabel(wordCount: number | null | undefined): string | null {
	const minutes = readingMinutes(wordCount);

	return minutes === null ? null : `${minutes} min`;
}
