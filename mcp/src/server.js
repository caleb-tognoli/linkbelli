#!/usr/bin/env node
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { ApiError, clampPage, createClient, trimArticle } from './client.js';
import {
	formatArticle,
	formatHits,
	formatItems,
	formatPlaylists
} from './format.js';

/**
 * A Linkbelli library, as something an agent can work with.
 *
 * The reason this is worth having is the article text: Linkbelli keeps the readable body of every
 * page at the moment it was saved, so an agent can answer questions from what somebody actually
 * read rather than from whatever the address still resolves to today.
 */
export function createServer(client, { readOnly = false } = {}) {
	const server = new McpServer({ name: 'linkbelli', version: '1.0.0' });

	/** Every tool answers the same way, including when it fails — a thrown error tells a model nothing. */
	const reply = (text, isError = false) => ({
		content: [{ type: 'text', text }],
		...(isError ? { isError: true } : {})
	});

	const guard = (fn) => async (args) => {
		try {
			return await fn(args);
		} catch (error) {
			if (error instanceof ApiError) return reply(error.message, true);

			// Anything else is a bug here rather than an answer from Linkbelli, and saying which
			// saves whoever reads the transcript from blaming the wrong thing.
			return reply(`The Linkbelli MCP server failed: ${error.message}`, true);
		}
	};

	server.registerTool(
		'search_links',
		{
			title: 'Search saved links',
			description:
				'Search everything saved in this Linkbelli account. Matches titles, notes and the ' +
				'full text of saved articles, so it finds a page by something said inside it. ' +
				'Returns linkId (to read the article) and itemId (to change the item).',
			inputSchema: {
				q: z.string().optional().describe('Text to look for. Omit to list by the other filters alone.'),
				host: z.string().optional().describe('Only links from this site, e.g. "bbc.co.uk".'),
				tag: z.array(z.string()).optional().describe('Only playlists carrying all of these tags.'),
				itemTag: z.array(z.string()).optional().describe('Only links carrying all of these tags.'),
				status: z.enum(['Added', 'Watched']).optional(),
				kind: z.enum(['Article', 'Video', 'Audio', 'Image', 'Document', 'Unknown']).optional(),
				minScore: z.number().int().min(0).max(100).optional(),
				broken: z.boolean().optional().describe('Only links whose page is gone or unreadable.'),
				sort: z.string().optional(),
				limit: z.number().int().min(1).max(50).optional(),
				cursor: z.string().optional().describe('From a previous result, to get the next page.')
			},
			annotations: { readOnlyHint: true }
		},
		guard(async (args) => {
			const page = await client.search(args);
			return reply(formatHits(page.items, { total: page.total, nextCursor: page.nextCursor }));
		})
	);

	server.registerTool(
		'read_article',
		{
			title: 'Read a saved article',
			description:
				'The readable text of a saved page, as it was when it was saved — so this still ' +
				'works after the original has changed or gone. Takes a linkId from search_links or ' +
				'list_playlist_items.',
			inputSchema: {
				linkId: z.string().describe('The link to read, from a search result.')
			},
			annotations: { readOnlyHint: true }
		},
		guard(async ({ linkId }) => {
			const content = await client.getContent(linkId);
			const { paragraphs, truncated } = trimArticle(content.paragraphs);

			return reply(formatArticle(content, paragraphs, truncated));
		})
	);

	server.registerTool(
		'list_playlists',
		{
			title: 'List playlists',
			description: 'The playlists in this account, with how much is in each and how visible it is.',
			inputSchema: { limit: z.number().int().min(1).max(50).optional() },
			annotations: { readOnlyHint: true }
		},
		guard(async ({ limit }) => {
			const page = await client.listPlaylists(limit);
			return reply(formatPlaylists(page.items));
		})
	);

	server.registerTool(
		'list_playlist_items',
		{
			title: 'List what is in a playlist',
			description:
				'The links in one playlist, in its own order. Use search_links to find something ' +
				'across all of them instead.',
			inputSchema: {
				playlistId: z.string(),
				limit: z.number().int().min(1).max(50).optional(),
				cursor: z.string().optional()
			},
			annotations: { readOnlyHint: true }
		},
		guard(async ({ playlistId, limit, cursor }) => {
			// Fetched for its name: a list of links under a heading of "the playlist" is harder to
			// talk about than one under the name the person gave it.
			const playlist = await client.getPlaylist(playlistId);
			const page = await client.listItems(playlistId, clampPage(limit), cursor);

			return reply(formatItems(playlist.name, page.items, page.nextCursor));
		})
	);

	if (!readOnly) {
		server.registerTool(
			'save_link',
			{
				title: 'Save a link',
				description:
					'Add a URL to a playlist. The page is fetched and its text saved shortly after, ' +
					'so it will not be readable by read_article immediately.',
				inputSchema: {
					playlistId: z.string().describe('From list_playlists.'),
					url: z.string().describe('The address to save.'),
					note: z.string().optional().describe('Why it is worth saving.')
				},
				annotations: { readOnlyHint: false, idempotentHint: true }
			},
			guard(async ({ playlistId, url, note }) => {
				try {
					const item = await client.addItem(playlistId, url, note);
					return reply(`Saved ${url}.\nitemId: ${item.id}`);
				} catch (error) {
					// Already there is the expected outcome of saving something twice, and reporting
					// it as a failure would invite a model to go round again.
					if (error instanceof ApiError && error.status === 409) {
						return reply(`${url} is already in that playlist.`);
					}
					throw error;
				}
			})
		);

		server.registerTool(
			'create_playlist',
			{
				title: 'Create a playlist',
				description:
					'Make a new playlist. Private unless told otherwise — nothing here publishes ' +
					'somebody’s links on their behalf.',
				inputSchema: {
					name: z.string(),
					description: z.string().optional(),
					visibility: z.enum(['Private', 'Unlisted', 'Public']).optional()
				},
				annotations: { readOnlyHint: false }
			},
			guard(async ({ name, description, visibility }) => {
				const playlist = await client.createPlaylist(name, description, visibility);

				return reply(`Created "${playlist.name}".\nplaylistId: ${playlist.id}`);
			})
		);

		server.registerTool(
			'mark_items',
			{
				title: 'Mark links watched or unwatched',
				description: 'Set the status of one or more items, by itemId.',
				inputSchema: {
					itemIds: z.array(z.string()).min(1).max(100),
					status: z.enum(['Added', 'Watched'])
				},
				annotations: { readOnlyHint: false, idempotentHint: true }
			},
			guard(async ({ itemIds, status }) => {
				const result = await client.setStatus(itemIds, status);
				const skipped = result.skipped ? ` ${result.skipped} were already that way or not yours.` : '';

				return reply(`Marked ${result.affected} ${status === 'Watched' ? 'watched' : 'unwatched'}.${skipped}`);
			})
		);
	}

	return server;
}

async function main() {
	const readOnly = process.env.LINKBELLI_MCP_READONLY === '1';

	let client;
	try {
		client = createClient({
			baseUrl: process.env.LINKBELLI_API_URL ?? 'http://localhost:5180',
			apiKey: process.env.LINKBELLI_API_KEY
		});
	} catch (error) {
		// stderr, never stdout: stdout is the protocol channel, and a friendly message written
		// there would be read as a malformed frame.
		process.stderr.write(`${error.message}\n`);
		process.exit(1);
	}

	const server = createServer(client, { readOnly });
	await server.connect(new StdioServerTransport());
}

// Only when run directly, so the tests can import createServer without starting a transport.
// Compared as resolved paths rather than by name: two files called server.js is not far-fetched.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
	main().catch((error) => {
		process.stderr.write(`${error.stack ?? error}` + '\n');
		process.exit(1);
	});
}
