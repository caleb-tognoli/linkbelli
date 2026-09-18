/** A passage somebody marked in a saved article, as the API returns it. */
export interface Highlight {
	id: string;
	linkId: string;
	paragraphIndex: number;
	start: number;
	end: number;
	text: string;
	note: string | null;
	createdAt: string;
	/** The article moved under it: the offsets no longer find these words. */
	orphaned: boolean;
}

/** A highlight with enough of its article to be read on its own, as the full list returns it. */
export interface HighlightWithSource {
	id: string;
	linkId: string;
	url: string;
	title: string | null;
	siteName: string | null;
	host: string;
	text: string;
	note: string | null;
	paragraphIndex: number;
	createdAt: string;
}

/** One run of a paragraph, either plain or covered by at least one highlight. */
export interface Segment {
	text: string;
	/** The highlights covering this run, outermost first. Empty for plain text. */
	ids: string[];
}

/**
 * Cuts a paragraph into runs so the marked parts can be wrapped and the rest left alone.
 *
 * Overlaps are handled by cutting at every boundary rather than by picking a winner. Two people
 * marking the same sentence from different ends is not an error, and neither is marking a phrase
 * inside a passage you already marked — the overlap is simply covered by both, and a click on it
 * needs to know that.
 */
export function toSegments(text: string, highlights: Highlight[]): Segment[] {
	const inside = highlights.filter(
		(h) => !h.orphaned && h.start >= 0 && h.end <= text.length && h.start < h.end
	);
	if (inside.length === 0) return [{ text, ids: [] }];

	const cuts = new Set<number>([0, text.length]);
	for (const h of inside) {
		cuts.add(h.start);
		cuts.add(h.end);
	}

	const points = [...cuts].sort((a, b) => a - b);
	const segments: Segment[] = [];

	for (let i = 0; i < points.length - 1; i++) {
		const from = points[i];
		const to = points[i + 1];
		if (from === to) continue;

		segments.push({
			text: text.slice(from, to),
			// Longest first, so a phrase marked inside a passage is the one a click lands on.
			ids: inside
				.filter((h) => h.start <= from && h.end >= to)
				.sort((a, b) => b.end - b.start - (a.end - a.start))
				.map((h) => h.id)
		});
	}

	return segments;
}

/**
 * Where a point in the DOM falls in the paragraph's own text.
 *
 * The paragraph is not one text node once anything in it is marked, so a selection offset is
 * relative to whichever run it landed in. This walks the runs in order and adds up what came
 * before, turning a DOM position back into the offset the API stores.
 *
 * Null when the node is not inside this paragraph at all, which is how a selection spanning two
 * paragraphs is detected.
 */
export function offsetIn(paragraph: Node, node: Node, offset: number): number | null {
	if (!paragraph.contains(node)) return null;

	const text = node.nodeType === Node.TEXT_NODE;

	// Everything before this node, in reading order.
	let before = 0;
	const walker = document.createTreeWalker(paragraph, NodeFilter.SHOW_TEXT);
	let current = walker.nextNode();

	while (current) {
		if (text ? current === node : node.contains(current)) break;
		before += current.textContent?.length ?? 0;
		current = walker.nextNode();
	}

	if (text) return before + offset;

	// A selection can land on an element rather than a text node — usually at a boundary, where
	// the offset counts child nodes instead of characters.
	let within = 0;
	for (let i = 0; i < offset && i < node.childNodes.length; i++) {
		within += node.childNodes[i].textContent?.length ?? 0;
	}

	return before + within;
}

/** The words themselves, trimmed of the whitespace a drag usually picks up at the ends. */
export function tidy(text: string, start: number, end: number): { start: number; end: number } {
	let from = start;
	let to = end;

	while (from < to && /\s/.test(text[from])) from++;
	while (to > from && /\s/.test(text[to - 1])) to--;

	return { start: from, end: to };
}
