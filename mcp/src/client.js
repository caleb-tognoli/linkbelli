/**
 * The thin layer between the MCP tools and the Linkbelli API.
 *
 * Kept apart from the tool definitions so the awkward parts — what counts as an error, how a
 * page of results is walked, what gets trimmed before it reaches a model — can be tested without
 * standing up a protocol server.
 */

/** Raised when the API answered, but not with what was asked for. */
export class ApiError extends Error {
	constructor(status, message) {
		super(message);
		this.name = 'ApiError';
		this.status = status;
	}
}

/**
 * How much article text one tool call will return.
 *
 * An article goes into a model's context window, and a 60 000-character one would crowd out the
 * conversation it was fetched for. Truncation is reported rather than hidden, so the model knows
 * it is reading part of something.
 */
export const MAX_ARTICLE_CHARS = 12_000;

/** A ceiling on any listing, whatever the caller asks for. */
export const MAX_PAGE = 50;

export function createClient({ baseUrl, apiKey, fetchImpl = fetch }) {
	if (!baseUrl) throw new Error('LINKBELLI_API_URL is required.');
	if (!apiKey) throw new Error('LINKBELLI_API_KEY is required.');

	const root = baseUrl.replace(/\/+$/, '');

	async function request(method, path, body) {
		let res;
		try {
			res = await fetchImpl(`${root}/api/v1${path}`, {
				method,
				headers: {
					'x-api-key': apiKey,
					...(body === undefined ? {} : { 'content-type': 'application/json' })
				},
				body: body === undefined ? undefined : JSON.stringify(body)
			});
		} catch (cause) {
			// A server that is not running is the single most likely thing to go wrong here, and
			// "fetch failed" tells whoever configured this nothing about which address failed.
			throw new ApiError(0, `Could not reach Linkbelli at ${root}. Is it running?`);
		}

		if (res.status === 401 || res.status === 403) {
			throw new ApiError(
				res.status,
				'Linkbelli refused the API key. Check LINKBELLI_API_KEY, and that its scopes cover this.'
			);
		}

		if (res.status === 404) throw new ApiError(404, 'Not found.');

		if (!res.ok) {
			const detail = await readProblem(res);
			throw new ApiError(res.status, detail ?? `Linkbelli returned ${res.status}.`);
		}

		if (res.status === 204) return null;

		return res.json();
	}

	/** Pulls the useful sentence out of a ProblemDetails body, if there is one. */
	async function readProblem(res) {
		try {
			const body = await res.json();
			if (typeof body?.detail === 'string') return body.detail;

			// Before the title, not after: a validation problem's title is the generic "one or
			// more validation errors occurred", and the field message underneath it is the answer.
			if (body?.errors && typeof body.errors === 'object') {
				const first = Object.values(body.errors).flat()[0];
				if (typeof first === 'string') return first;
			}

			if (typeof body?.title === 'string') return body.title;
		} catch {
			// Not JSON, or no body at all.
		}

		return null;
	}

	return {
		request,

		listPlaylists: (limit) => request('GET', `/playlists?limit=${clampPage(limit)}`),

		getPlaylist: (playlistId) => request('GET', `/playlists/${playlistId}`),

		listItems: (playlistId, limit, cursor) =>
			request(
				'GET',
				`/playlists/${playlistId}/items?limit=${clampPage(limit)}` +
					(cursor ? `&cursor=${encodeURIComponent(cursor)}` : '')
			),

		search: (query) => request('GET', `/search?${searchParams(query)}`),

		getContent: (linkId) => request('GET', `/links/${linkId}/content`),

		listHighlights: (limit, cursor) =>
			request(
				'GET',
				`/highlights?limit=${clampPage(limit)}` +
					(cursor ? `&cursor=${encodeURIComponent(cursor)}` : '')
			),

		listHighlightsFor: (linkId) => request('GET', `/links/${linkId}/highlights`),

		addItem: (playlistId, url, note) =>
			request('POST', `/playlists/${playlistId}/items`, { url, note: note ?? null }),

		createPlaylist: (name, description, visibility) =>
			request('POST', '/playlists', {
				name,
				description: description ?? null,
				visibility: visibility ?? 'Private'
			}),

		setStatus: (itemIds, status) =>
			request('POST', '/items/bulk', { itemIds, action: 'SetStatus', status })
	};
}

/** Keeps a listing to a size worth putting in front of a model. */
export function clampPage(limit) {
	const n = Number(limit);
	if (!Number.isFinite(n) || n < 1) return 20;

	return Math.min(Math.floor(n), MAX_PAGE);
}

/** Builds a search query string, leaving out everything that was not asked about. */
export function searchParams(query = {}) {
	const params = new URLSearchParams();

	if (query.q) params.set('q', query.q);
	if (query.host) params.set('host', query.host);
	if (query.status) params.set('status', query.status);
	if (query.kind) params.set('kind', query.kind);
	if (query.sort) params.set('sort', query.sort);
	if (query.minScore !== undefined && query.minScore !== null) {
		params.set('minScore', String(query.minScore));
	}
	if (query.broken === true) params.set('broken', 'true');
	for (const tag of query.tag ?? []) params.append('tag', tag);
	for (const tag of query.itemTag ?? []) params.append('itemTag', tag);

	params.set('limit', String(clampPage(query.limit)));
	if (query.cursor) params.set('cursor', query.cursor);

	return params.toString();
}

/**
 * An article, trimmed to something that fits in a reply.
 *
 * Cut at a paragraph boundary rather than mid-sentence: a model reading half a sentence will
 * finish it for itself, which is the one failure mode worth spending a few characters to avoid.
 */
export function trimArticle(paragraphs, max = MAX_ARTICLE_CHARS) {
	const kept = [];
	let length = 0;

	for (const paragraph of paragraphs ?? []) {
		if (length + paragraph.length > max && kept.length > 0) {
			return { paragraphs: kept, truncated: true };
		}

		kept.push(paragraph);
		length += paragraph.length;

		// One paragraph longer than the whole budget still has to be cut somewhere.
		if (length > max) return { paragraphs: kept, truncated: true };
	}

	return { paragraphs: kept, truncated: false };
}
