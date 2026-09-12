/**
 * A manual smoke test against a running Linkbelli, over a real stdio transport.
 *
 * Not part of the test suite: it needs a server and a key. Run it after changing anything that
 * touches the wire.
 *
 *   LINKBELLI_API_KEY=... node live-check.mjs
 */
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const transport = new StdioClientTransport({
	command: process.execPath,
	args: ['src/server.js'],
	env: {
		...process.env,
		LINKBELLI_API_URL: process.env.LINKBELLI_API_URL ?? 'http://localhost:5180',
		LINKBELLI_API_KEY: process.env.LINKBELLI_API_KEY
	}
});

const mcp = new Client({ name: 'live-check', version: '1.0.0' });
await mcp.connect(transport);

const text = (r) => r.content.map((c) => c.text).join('\n');
const show = (label, value) => console.log(`\n=== ${label} ===\n${value}`);

show('tools', (await mcp.listTools()).tools.map((t) => t.name).join(', '));

const playlists = await mcp.callTool({ name: 'list_playlists', arguments: {} });
show('list_playlists', text(playlists));

const created = await mcp.callTool({
	name: 'create_playlist',
	arguments: { name: `MCP check ${Date.now()}` }
});
show('create_playlist', text(created));
const playlistId = text(created).match(/playlistId: (\S+)/)?.[1];

show(
	'save_link',
	text(
		await mcp.callTool({
			name: 'save_link',
			arguments: {
				playlistId,
				url: 'https://www.bbc.co.uk/news',
				note: 'saved by the MCP server'
			}
		})
	)
);

// Again, to prove a duplicate is not reported as a failure.
show(
	'save_link (same one again)',
	text(await mcp.callTool({ name: 'save_link', arguments: { playlistId, url: 'https://www.bbc.co.uk/news' } }))
);

show('save_link (rubbish address)', text(
	await mcp.callTool({ name: 'save_link', arguments: { playlistId, url: 'not-a-url' } })
));

show('search_links', text(await mcp.callTool({ name: 'search_links', arguments: { q: 'the', limit: 3 } })));

// Read whatever the search turned up, to prove the article text comes back.
const hits = text(await mcp.callTool({ name: 'search_links', arguments: { limit: 5, kind: 'Article' } }));
const linkId = hits.match(/linkId: (\S+)/)?.[1];
if (linkId) {
	const article = text(await mcp.callTool({ name: 'read_article', arguments: { linkId } }));
	show('read_article', article.slice(0, 600) + (article.length > 600 ? '\n…' : ''));
} else {
	show('read_article', 'no article-kind link to read');
}

show('read_article (missing)', text(
	await mcp.callTool({ name: 'read_article', arguments: { linkId: '00000000-0000-0000-0000-000000000000' } })
));

await mcp.close();
