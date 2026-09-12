import type { SourceType } from '$lib/types';

/** What a dry run of a source config came back with. */
export interface SourcePreview {
	count: number;
	links: { url: string; title: string | null }[];
}

/**
 * Config keys without which a preview cannot say anything.
 *
 * Asking the server to fetch a half-typed URL wastes a rate-limited request and answers a
 * question nobody asked — the person is still typing.
 */
const REQUIRED: Record<SourceType, string[]> = {
	Rss: ['feedUrl'],
	Scraper: ['url', 'itemSelector'],
	JsonApi: ['url', 'itemsPath'],
	// Nothing to fetch: a webhook source has no address to look at.
	Webhook: []
};

/** Whether this config is complete enough to be worth a live fetch. */
export function isPreviewable(type: SourceType, config: Record<string, string>): boolean {
	if (type === 'Webhook') return false;

	return REQUIRED[type].every((key) => {
		const value = config[key]?.trim() ?? '';
		if (!value) return false;

		// A URL that is still being typed — "https://ex" — is not worth fetching.
		if (key.toLowerCase().endsWith('url')) return /^https?:\/\/[^\s.]+\.[^\s]/i.test(value);

		return true;
	});
}

/**
 * A stable key for one config, so an unchanged one is not fetched twice.
 *
 * The preview endpoint does a live outbound fetch and is rate-limited accordingly; re-running it
 * because a field was focused and blurred would spend that budget on nothing.
 */
export function previewKey(type: SourceType, config: Record<string, string>): string {
	const entries = Object.entries(config)
		.map(([key, value]) => [key, value?.trim() ?? ''] as const)
		.filter(([, value]) => value.length > 0)
		.sort(([a], [b]) => a.localeCompare(b));

	return JSON.stringify([type, entries]);
}
