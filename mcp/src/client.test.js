import { describe, expect, it, vi } from 'vitest';
import {
	ApiError,
	clampPage,
	createClient,
	MAX_PAGE,
	searchParams,
	trimArticle
} from './client.js';

const ok = (body, status = 200) =>
	new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

describe('createClient', () => {
	it('refuses to start without somewhere to talk to', () => {
		expect(() => createClient({ apiKey: 'k' })).toThrow(/LINKBELLI_API_URL/);
		expect(() => createClient({ baseUrl: 'http://x' })).toThrow(/LINKBELLI_API_KEY/);
	});

	it('sends the key as a header, never in the address', async () => {
		const fetchImpl = vi.fn(async () => ok({ items: [] }));
		await createClient({ baseUrl: 'http://x', apiKey: 'secret', fetchImpl }).listPlaylists();

		const [url, init] = fetchImpl.mock.calls[0];
		// A key in a query string ends up in logs and browser history.
		expect(url).not.toContain('secret');
		expect(init.headers['x-api-key']).toBe('secret');
	});

	it('does not double the slash when the base url has a trailing one', async () => {
		const fetchImpl = vi.fn(async () => ok({ items: [] }));
		await createClient({ baseUrl: 'http://x/', apiKey: 'k', fetchImpl }).listPlaylists();

		expect(fetchImpl.mock.calls[0][0]).toBe('http://x/api/v1/playlists?limit=20');
	});

	it('says where it could not reach, rather than "fetch failed"', async () => {
		const fetchImpl = async () => {
			throw new TypeError('fetch failed');
		};
		const client = createClient({ baseUrl: 'http://nope:9', apiKey: 'k', fetchImpl });

		await expect(client.listPlaylists()).rejects.toThrow(/Could not reach Linkbelli at http:\/\/nope:9/);
	});

	it('explains a rejected key instead of passing on a bare 401', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => new Response('', { status: 401 })
		});

		await expect(client.listPlaylists()).rejects.toThrow(/LINKBELLI_API_KEY/);
	});

	it('mentions scopes on a 403, which is what a scoped key gets wrong', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => new Response('', { status: 403 })
		});

		await expect(client.listPlaylists()).rejects.toThrow(/scopes/);
	});

	it('surfaces the useful sentence out of a problem response', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => ok({ title: 'Bad Request', detail: 'That is not a web address.' }, 400)
		});

		await expect(client.listPlaylists()).rejects.toThrow('That is not a web address.');
	});

	it('digs the message out of a validation problem', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => ok({ title: 'Validation', errors: { url: ['Url is required.'] } }, 400)
		});

		await expect(client.listPlaylists()).rejects.toThrow('Url is required.');
	});

	it('does not fall over on an error with no body', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => new Response('', { status: 500 })
		});

		await expect(client.listPlaylists()).rejects.toThrow(/500/);
	});

	it('keeps the status, so a caller can tell 409 from a real failure', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => ok({ detail: 'Already there.' }, 409)
		});

		await expect(client.addItem('p', 'https://x')).rejects.toMatchObject({
			name: 'ApiError',
			status: 409
		});
	});

	it('treats a 204 as nothing rather than trying to parse it', async () => {
		const client = createClient({
			baseUrl: 'http://x',
			apiKey: 'k',
			fetchImpl: async () => new Response(null, { status: 204 })
		});

		await expect(client.listPlaylists()).resolves.toBeNull();
	});
});

describe('clampPage', () => {
	it('has a sensible default', () => {
		expect(clampPage(undefined)).toBe(20);
		expect(clampPage(null)).toBe(20);
		expect(clampPage('nonsense')).toBe(20);
		expect(clampPage(0)).toBe(20);
		expect(clampPage(-5)).toBe(20);
	});

	it('takes what was asked for, up to the ceiling', () => {
		expect(clampPage(5)).toBe(5);
		expect(clampPage(1000)).toBe(MAX_PAGE);
	});
});

describe('searchParams', () => {
	it('leaves out everything nobody asked about', () => {
		expect(searchParams({ q: 'rust' })).toBe('q=rust&limit=20');
	});

	it('repeats a tag filter per tag, as the API reads them', () => {
		const params = searchParams({ tag: ['a', 'b'], itemTag: ['c'] });

		expect(params).toContain('tag=a&tag=b');
		expect(params).toContain('itemTag=c');
	});

	it('passes a zero score through rather than treating it as absent', () => {
		// minScore=0 is a real filter, and `if (query.minScore)` would have dropped it.
		expect(searchParams({ minScore: 0 })).toContain('minScore=0');
	});

	it('only asks for broken when broken was asked for', () => {
		expect(searchParams({ broken: false })).not.toContain('broken');
		expect(searchParams({ broken: true })).toContain('broken=true');
	});

	it('escapes a term that would otherwise break the query string', () => {
		expect(searchParams({ q: 'a&b=c d' })).toContain('q=a%26b%3Dc+d');
	});
});

describe('trimArticle', () => {
	it('keeps a short article whole, and says so', () => {
		const paragraphs = ['One.', 'Two.'];

		expect(trimArticle(paragraphs, 100)).toEqual({ paragraphs, truncated: false });
	});

	it('cuts at a paragraph rather than mid-sentence', () => {
		const result = trimArticle(['a'.repeat(60), 'b'.repeat(60), 'c'.repeat(60)], 100);

		expect(result.paragraphs).toEqual(['a'.repeat(60)]);
		expect(result.truncated).toBe(true);
	});

	it('still cuts one paragraph longer than the whole budget', () => {
		// Otherwise a single enormous block would ignore the limit entirely.
		const result = trimArticle(['x'.repeat(500)], 100);

		expect(result.paragraphs).toHaveLength(1);
		expect(result.truncated).toBe(true);
	});

	it('copes with an article that has no text at all', () => {
		expect(trimArticle(undefined)).toEqual({ paragraphs: [], truncated: false });
		expect(trimArticle([])).toEqual({ paragraphs: [], truncated: false });
	});
});

describe('ApiError', () => {
	it('carries the status alongside the message', () => {
		const error = new ApiError(409, 'Already there.');

		expect(error.status).toBe(409);
		expect(error.message).toBe('Already there.');
		expect(error).toBeInstanceOf(Error);
	});
});
