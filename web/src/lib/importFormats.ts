/**
 * Reading the files people already have.
 *
 * The page took a CSV and nothing else — not even this app's own HTML export, which Settings
 * offers and nothing could read back, while the home page promised "a browser bookmark export or
 * a plain list of URLs". Kept apart from the route so each format can be tested on its own.
 */

export interface ImportRow {
	url: string;
	note?: string;
}

function parseLine(line: string): string[] {
	const result: string[] = [];
	let current = '';
	let inQuotes = false;

	for (let i = 0; i < line.length; i++) {
		const c = line[i];
		if (c === '"') {
			if (inQuotes && line[i + 1] === '"') {
				current += '"';
				i++;
			} else {
				inQuotes = !inQuotes;
			}
		} else if (c === ',' && !inQuotes) {
			result.push(current);
			current = '';
		} else {
			current += c;
		}
	}
	result.push(current);
	return result;
}

export function parseCsv(text: string): ImportRow[] {
	const lines = text.split(/\r?\n/).filter((l) => l.trim());
	if (lines.length < 2) return [];

	const headers = parseLine(lines[0]).map((h) => h.toLowerCase().trim());
	const urlIdx = headers.indexOf('url');
	if (urlIdx === -1) return [];
	const noteIdx = headers.indexOf('note');

	return lines
		.slice(1)
		.map((line) => parseLine(line))
		.filter((cols) => cols[urlIdx]?.trim())
		.map((cols) => ({
			url: cols[urlIdx].trim(),
			...(noteIdx >= 0 && cols[noteIdx]?.trim() ? { note: cols[noteIdx].trim() } : {})
		}));
}

/**
 * A browser's bookmark export.
 *
 * The home page has always said links could be brought in "from a browser bookmark export", and
 * the page accepted CSV and nothing else — including its own HTML export, which could be
 * downloaded from Settings and then not read back. Netscape bookmark files are what every
 * browser writes: a nest of `<DT><A HREF="…">Title</A>` with no closing tags to speak of, so
 * they are read by finding the anchors rather than by parsing the document.
 */
export function parseBookmarks(text: string): ImportRow[] {
	const rows: ImportRow[] = [];
	const anchor = /<a\s[^>]*href\s*=\s*["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi;

	for (const match of text.matchAll(anchor)) {
		const url = match[1].trim();
		// Bookmark bars hold internal addresses too; only what the API could fetch is worth sending.
		if (!/^https?:\/\//i.test(url)) continue;
		const title = match[2]
			.replace(/<[^>]*>/g, '')
			.replace(/&amp;/g, '&')
			.replace(/&lt;/g, '<')
			.replace(/&gt;/g, '>')
			.replace(/&quot;/g, '"')
			.replace(/&#39;/g, "'")
			.replace(/\s+/g, ' ')
			.trim();
		rows.push({ url, ...(title ? { note: title } : {}) });
	}

	return rows;
}

/** One address per line — what "a plain list of URLs" means, and what a paste usually is. */
export function parseUrlList(text: string): ImportRow[] {
	return text
		.split(/\r?\n/)
		.map((line) => line.trim())
		.filter((line) => /^https?:\/\//i.test(line))
		.map((url) => ({ url }));
}

/**
 * Reads whatever was handed over.
 *
 * By content rather than by file extension: people rename files, and a bookmark export saved as
 * .txt is still a bookmark export.
 */
export function parseRows(text: string, filename: string): ImportRow[] {
	if (/<a\s[^>]*href/i.test(text)) return parseBookmarks(text);

	const csv = parseCsv(text);
	if (csv.length > 0) return csv;

	// A CSV whose header says "url" but has no rows is not a URL list; anything else might be.
	return filename.toLowerCase().endsWith('.csv') && /^\s*url\b/i.test(text) ? [] : parseUrlList(text);
}
