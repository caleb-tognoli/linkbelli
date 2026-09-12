/**
 * Turning API responses into text a model can actually use.
 *
 * MCP tools return text, and handing back raw JSON wastes the window on punctuation and field
 * names the model has to parse anyway. These render the same information in the shape somebody
 * would write it down: one line per thing, ids kept because the next tool call needs them.
 */

/** One search hit, as a line. */
export function formatHit(hit) {
	const link = hit.link ?? {};
	const title = link.title || link.url || '(untitled)';
	const bits = [`- ${title}`, `  ${link.url ?? ''}`];

	const meta = [];
	if (hit.playlistName) meta.push(`in ${hit.playlistName}`);
	if (hit.status && hit.status !== 'Added') meta.push(hit.status.toLowerCase());
	if (typeof hit.score === 'number') meta.push(`score ${hit.score}`);
	if (link.wordCount) meta.push(`${link.wordCount} words`);
	if (hit.tags?.length) meta.push(hit.tags.join(', '));
	if (meta.length) bits.push(`  ${meta.join(' · ')}`);

	// The note is usually the reason it was saved, which is the part worth reading.
	if (hit.note) bits.push(`  note: ${hit.note}`);

	// Only present when the term was found in the article rather than the title, so it is the
	// only thing explaining why this is in the results at all.
	if (hit.snippet) bits.push(`  …${hit.snippet}…`);

	// Both ids: one to read the text, the other to change the item.
	if (link.id) bits.push(`  linkId: ${link.id}`);
	if (hit.itemId) bits.push(`  itemId: ${hit.itemId}`);

	return bits.join('\n');
}

export function formatHits(hits, { total, nextCursor } = {}) {
	if (!hits?.length) return 'Nothing matched.';

	const head =
		typeof total === 'number' && total > hits.length
			? `${hits.length} of ${total} matches:`
			: `${hits.length} ${hits.length === 1 ? 'match' : 'matches'}:`;

	const body = hits.map(formatHit).join('\n');
	const tail = nextCursor ? `\n\nMore results: pass cursor "${nextCursor}".` : '';

	return `${head}\n${body}${tail}`;
}

export function formatPlaylist(playlist) {
	const bits = [`- ${playlist.name}`];

	const meta = [`${playlist.itemCount ?? 0} links`, (playlist.visibility ?? '').toLowerCase()];
	if (playlist.pendingCount) meta.push(`${playlist.pendingCount} still being fetched`);
	if (playlist.tags?.length) meta.push(playlist.tags.join(', '));
	bits.push(`  ${meta.filter(Boolean).join(' · ')}`);

	if (playlist.description) bits.push(`  ${playlist.description}`);
	bits.push(`  playlistId: ${playlist.id}`);

	return bits.join('\n');
}

export function formatPlaylists(playlists) {
	if (!playlists?.length) return 'No playlists yet.';

	return `${playlists.length} ${playlists.length === 1 ? 'playlist' : 'playlists'}:\n${playlists
		.map(formatPlaylist)
		.join('\n')}`;
}

/** One item in a playlist. Close to a search hit, without the playlist name it is already under. */
export function formatItem(item) {
	const link = item.link ?? {};
	const bits = [`- ${link.title || link.url || '(untitled)'}`, `  ${link.url ?? ''}`];

	const meta = [];
	if (item.status && item.status !== 'Added') meta.push(item.status.toLowerCase());
	if (typeof item.score === 'number') meta.push(`score ${item.score}`);
	if (link.wordCount) meta.push(`${link.wordCount} words`);
	if (meta.length) bits.push(`  ${meta.join(' · ')}`);
	if (item.note) bits.push(`  note: ${item.note}`);
	if (link.id) bits.push(`  linkId: ${link.id}`);
	if (item.id) bits.push(`  itemId: ${item.id}`);

	return bits.join('\n');
}

export function formatItems(playlistName, items, nextCursor) {
	if (!items?.length) return `${playlistName} is empty.`;

	const tail = nextCursor ? `\n\nMore: pass cursor "${nextCursor}".` : '';

	return `${playlistName} — ${items.length} shown:\n${items.map(formatItem).join('\n')}${tail}`;
}

/** A saved article, as something to read rather than a record to parse. */
export function formatArticle(content, paragraphs, truncated) {
	const head = [content.title || content.url, content.url];
	if (content.siteName) head.push(content.siteName);

	const note = truncated
		? `\n\n[Cut short here. The full article is ${content.wordCount} words.]`
		: content.truncated
			? `\n\n[Only the start of the article was saved. The whole thing is ${content.wordCount} words.]`
			: '';

	if (!paragraphs.length) {
		return `${head.join('\n')}\n\n[No readable text was saved for this one — it may be a video, or a page nothing could be extracted from.]`;
	}

	return `${head.join('\n')}\n\n${paragraphs.join('\n\n')}${note}`;
}
