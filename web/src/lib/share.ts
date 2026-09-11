/**
 * Where a shared link actually is. A bookmarklet puts it in `url`; Android's share sheet often
 * puts it in `text` instead, and sometimes buries it inside a sentence of the person's own words.
 */
export function sharedUrl(params: URLSearchParams): string {
	return params.get('url') ?? extractUrl(params.get('text')) ?? '';
}

/** The first web address in some text, or null when there isn't one. */
export function extractUrl(text: string | null): string | null {
	if (!text) return null;

	const match = text.match(/https?:\/\/\S+/);
	return match ? match[0] : null;
}
