import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError } from './client.js';
import { createServer } from './server.js';

/**
 * A stand-in for the API layer. The point of these tests is the protocol surface — what tools
 * exist, what they answer, and what they do when the API says no — so the HTTP itself is
 * covered separately in client.test.js.
 */
function fakeClient(over = {}) {
	return {
		listPlaylists: vi.fn(async () => ({ items: [] })),
		getPlaylist: vi.fn(async () => ({ id: 'p1', name: 'Reading' })),
		listItems: vi.fn(async () => ({ items: [], nextCursor: null })),
		search: vi.fn(async () => ({ items: [], total: 0, nextCursor: null })),
		getContent: vi.fn(async () => ({
			id: 'l1',
			url: 'https://example.com/a',
			host: 'example.com',
			title: 'An article',
			siteName: 'Example',
			paragraphs: ['First paragraph.', 'Second paragraph.'],
			wordCount: 4,
			truncated: false
		})),
		addItem: vi.fn(async () => ({ id: 'i1' })),
		createPlaylist: vi.fn(async () => ({ id: 'p2', name: 'New' })),
		setStatus: vi.fn(async () => ({ affected: 2, skipped: 0 })),
		...over
	};
}

/** Connects a real MCP client to the server over an in-memory pair. */
async function connect(client, options) {
	const server = createServer(client, options);
	const mcp = new Client({ name: 'test', version: '1.0.0' });
	const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();

	await Promise.all([server.connect(serverTransport), mcp.connect(clientTransport)]);

	return mcp;
}

const textOf = (result) => result.content.map((c) => c.text).join('\n');

describe('the tools on offer', () => {
	it('offers reading and writing by default', async () => {
		const mcp = await connect(fakeClient());
		const names = (await mcp.listTools()).tools.map((t) => t.name).sort();

		expect(names).toEqual([
			'create_playlist',
			'list_playlist_items',
			'list_playlists',
			'mark_items',
			'read_article',
			'save_link',
			'search_links'
		]);
	});

	it('offers only reading when told to', async () => {
		const mcp = await connect(fakeClient(), { readOnly: true });
		const names = (await mcp.listTools()).tools.map((t) => t.name).sort();

		// Not merely refused at call time — an agent should not see a tool it cannot use.
		expect(names).toEqual([
			'list_playlist_items',
			'list_playlists',
			'read_article',
			'search_links'
		]);
	});

	it('never offers a way to delete anything', async () => {
		const mcp = await connect(fakeClient());
		const names = (await mcp.listTools()).tools.map((t) => t.name);

		// An agent holding somebody's API key should not be able to throw their library away.
		expect(names.some((n) => /delete|remove|destroy|trash/i.test(n))).toBe(false);
	});

	it('marks the reading tools as read-only, so a client can say so', async () => {
		const mcp = await connect(fakeClient());
		const tools = (await mcp.listTools()).tools;

		expect(tools.find((t) => t.name === 'search_links').annotations.readOnlyHint).toBe(true);
		expect(tools.find((t) => t.name === 'save_link').annotations.readOnlyHint).toBe(false);
	});
});

describe('search_links', () => {
	it('passes the filters through and renders the hits', async () => {
		const client = fakeClient({
			search: vi.fn(async () => ({
				items: [
					{
						itemId: 'i1',
						playlistId: 'p1',
						playlistName: 'Reading',
						link: { id: 'l1', url: 'https://example.com/a', title: 'Rust at work', wordCount: 900 },
						note: 'for the talk',
						status: 'Added',
						score: 80,
						tags: ['rust'],
						snippet: 'borrow checker'
					}
				],
				total: 1,
				nextCursor: null
			}))
		});
		const mcp = await connect(client);

		const result = await mcp.callTool({ name: 'search_links', arguments: { q: 'rust', limit: 5 } });
		const text = textOf(result);

		expect(client.search).toHaveBeenCalledWith(expect.objectContaining({ q: 'rust', limit: 5 }));
		expect(text).toContain('Rust at work');
		expect(text).toContain('in Reading');
		expect(text).toContain('for the talk');
		expect(text).toContain('borrow checker');
		// Both ids, because the next call the model makes needs one of them.
		expect(text).toContain('linkId: l1');
		expect(text).toContain('itemId: i1');
	});

	it('says plainly when nothing matched', async () => {
		const mcp = await connect(fakeClient());

		expect(textOf(await mcp.callTool({ name: 'search_links', arguments: { q: 'nothing' } })))
			.toBe('Nothing matched.');
	});

	it('offers the cursor when there is more', async () => {
		const client = fakeClient({
			search: vi.fn(async () => ({
				items: [{ itemId: 'i1', link: { id: 'l1', url: 'https://x/a', title: 'A' } }],
				total: 40,
				nextCursor: 'abc123'
			}))
		});
		const mcp = await connect(client);

		const text = textOf(await mcp.callTool({ name: 'search_links', arguments: {} }));

		expect(text).toContain('1 of 40 matches');
		expect(text).toContain('cursor "abc123"');
	});
});

describe('read_article', () => {
	it('returns the saved text, not a record about it', async () => {
		const mcp = await connect(fakeClient());

		const text = textOf(await mcp.callTool({ name: 'read_article', arguments: { linkId: 'l1' } }));

		expect(text).toContain('An article');
		expect(text).toContain('First paragraph.');
		expect(text).toContain('Second paragraph.');
	});

	it('says so when a page had no readable text', async () => {
		const client = fakeClient({
			getContent: vi.fn(async () => ({
				id: 'l1',
				url: 'https://example.com/video',
				title: 'A video',
				paragraphs: [],
				wordCount: 0,
				truncated: false
			}))
		});
		const mcp = await connect(client);

		const text = textOf(await mcp.callTool({ name: 'read_article', arguments: { linkId: 'l1' } }));

		// Otherwise a model reads an empty reply as a failure and tries again.
		expect(text).toContain('No readable text');
	});

	it('admits when it has cut the article short', async () => {
		const client = fakeClient({
			getContent: vi.fn(async () => ({
				id: 'l1',
				url: 'https://example.com/long',
				title: 'Long',
				paragraphs: Array.from({ length: 200 }, () => 'x'.repeat(400)),
				wordCount: 20000,
				truncated: false
			}))
		});
		const mcp = await connect(client);

		const text = textOf(await mcp.callTool({ name: 'read_article', arguments: { linkId: 'l1' } }));

		expect(text).toContain('Cut short here');
		expect(text).toContain('20000 words');
	});

	it('reports a link that is not there as an error, not an empty article', async () => {
		const client = fakeClient({
			getContent: vi.fn(async () => {
				throw new ApiError(404, 'Not found.');
			})
		});
		const mcp = await connect(client);

		const result = await mcp.callTool({ name: 'read_article', arguments: { linkId: 'nope' } });

		expect(result.isError).toBe(true);
		expect(textOf(result)).toContain('Not found.');
	});
});

describe('save_link', () => {
	it('saves and hands back the id', async () => {
		const client = fakeClient();
		const mcp = await connect(client);

		const text = textOf(
			await mcp.callTool({
				name: 'save_link',
				arguments: { playlistId: 'p1', url: 'https://example.com/new', note: 'worth it' }
			})
		);

		expect(client.addItem).toHaveBeenCalledWith('p1', 'https://example.com/new', 'worth it');
		expect(text).toContain('itemId: i1');
	});

	it('treats a link already there as done, not as a failure', async () => {
		const client = fakeClient({
			addItem: vi.fn(async () => {
				throw new ApiError(409, 'Already there.');
			})
		});
		const mcp = await connect(client);

		const result = await mcp.callTool({
			name: 'save_link',
			arguments: { playlistId: 'p1', url: 'https://example.com/dupe' }
		});

		// Reported as an error, a model would try again — and again.
		expect(result.isError).toBeFalsy();
		expect(textOf(result)).toContain('already in that playlist');
	});

	it('passes a refusal back in words, without throwing', async () => {
		const client = fakeClient({
			addItem: vi.fn(async () => {
				throw new ApiError(400, 'That is not a web address.');
			})
		});
		const mcp = await connect(client);

		const result = await mcp.callTool({
			name: 'save_link',
			arguments: { playlistId: 'p1', url: 'not-a-url' }
		});

		expect(result.isError).toBe(true);
		expect(textOf(result)).toContain('That is not a web address.');
	});

	it('is not there at all in read-only mode', async () => {
		const client = fakeClient();
		const mcp = await connect(client, { readOnly: true });

		const result = await mcp.callTool({
			name: 'save_link',
			arguments: { playlistId: 'p1', url: 'https://x' }
		});

		// Refused by the protocol because the tool was never registered, so nothing this server
		// does could let a call through.
		expect(result.isError).toBe(true);
		expect(result.content[0].text).toContain('not found');
		expect(client.addItem).not.toHaveBeenCalled();
	});
});

describe('create_playlist', () => {
	it('defaults to private', async () => {
		const client = fakeClient();
		const mcp = await connect(client);

		await mcp.callTool({ name: 'create_playlist', arguments: { name: 'Reading' } });

		// Nothing here publishes somebody's links on their behalf.
		expect(client.createPlaylist).toHaveBeenCalledWith('Reading', undefined, undefined);
	});

	it('publishes only when asked to', async () => {
		const client = fakeClient();
		const mcp = await connect(client);

		await mcp.callTool({
			name: 'create_playlist',
			arguments: { name: 'Public one', visibility: 'Public' }
		});

		expect(client.createPlaylist).toHaveBeenCalledWith('Public one', undefined, 'Public');
	});
});

describe('mark_items', () => {
	it('reports what actually changed', async () => {
		const client = fakeClient({ setStatus: vi.fn(async () => ({ affected: 2, skipped: 1 })) });
		const mcp = await connect(client);

		const text = textOf(
			await mcp.callTool({ name: 'mark_items', arguments: { itemIds: ['a', 'b', 'c'], status: 'Watched' } })
		);

		expect(text).toContain('Marked 2 watched');
		expect(text).toContain('1 were already that way or not yours');
	});
});

describe('when Linkbelli is unreachable', () => {
	let mcp;

	beforeEach(async () => {
		mcp = await connect(
			fakeClient({
				listPlaylists: vi.fn(async () => {
					throw new ApiError(0, 'Could not reach Linkbelli at http://localhost:5180. Is it running?');
				})
			})
		);
	});

	it('says which address failed rather than throwing at the protocol', async () => {
		const result = await mcp.callTool({ name: 'list_playlists', arguments: {} });

		expect(result.isError).toBe(true);
		expect(textOf(result)).toContain('http://localhost:5180');
	});
});

describe('when the server itself has a bug', () => {
	it('says that it was this server, not Linkbelli', async () => {
		const mcp = await connect(
			fakeClient({
				search: vi.fn(async () => {
					throw new TypeError('cannot read properties of undefined');
				})
			})
		);

		const result = await mcp.callTool({ name: 'search_links', arguments: {} });

		// Blaming the wrong component sends whoever reads the transcript to the wrong logs.
		expect(textOf(result)).toContain('Linkbelli MCP server failed');
	});
});
