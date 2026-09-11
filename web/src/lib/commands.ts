import type { Playlist } from '$lib/types';
import { looksLikeUrl } from '$lib/urls';

/** One thing the palette can do. */
export interface Command {
	id: string;
	label: string;
	/** What it is, shown as a quiet chip — "Go", "Playlist", "Search", "Save". */
	kind: string;
	href: string;
	/** Extra words a query can match on, beyond the label. */
	keywords?: string;
}

/** The fixed destinations, always available. */
export const PLACES: Command[] = [
	{ id: 'go-playlists', label: 'Playlists', kind: 'Go', href: '/playlists', keywords: 'lists home' },
	{ id: 'go-search', label: 'Search', kind: 'Go', href: '/search', keywords: 'find look' },
	{ id: 'go-sources', label: 'Sources', kind: 'Go', href: '/sources', keywords: 'feeds scrapers' },
	{ id: 'go-discover', label: 'Discover', kind: 'Go', href: '/discover', keywords: 'public browse' },
	{ id: 'go-duplicates', label: 'Duplicates', kind: 'Go', href: '/duplicates', keywords: 'same twice' },
	{ id: 'go-trash', label: 'Trash', kind: 'Go', href: '/trash', keywords: 'deleted restore' },
	{ id: 'go-import', label: 'Import', kind: 'Go', href: '/import', keywords: 'csv bookmarks' },
	{ id: 'go-save', label: 'Save a link', kind: 'Go', href: '/save', keywords: 'add new url' },
	{ id: 'go-profile', label: 'Profile', kind: 'Go', href: '/profile', keywords: 'settings account keys' }
];

/**
 * What to offer for a query. Ordered by how directly each answers it: a pasted address is almost
 * certainly a thing to save, an exact-ish playlist name beats a fuzzy one, and searching
 * everything is always available as the fallback.
 */
export function buildCommands(query: string, playlists: Playlist[]): Command[] {
	const trimmed = query.trim();

	if (!trimmed) {
		return PLACES;
	}

	const commands: Command[] = [];

	// A pasted URL is unambiguous: nobody types https:// into a palette hoping to navigate.
	if (looksLikeUrl(trimmed)) {
		commands.push({
			id: 'save-url',
			label: `Save ${trimmed}`,
			kind: 'Save',
			href: `/save?url=${encodeURIComponent(trimmed)}`
		});
	}

	const needle = trimmed.toLowerCase();

	commands.push(
		...playlists
			.filter((playlist) => playlist.name.toLowerCase().includes(needle))
			// A name that starts with what was typed is a better answer than one that merely
			// contains it somewhere.
			.sort((a, b) => rank(a.name, needle) - rank(b.name, needle))
			.slice(0, 6)
			.map((playlist) => ({
				id: `playlist-${playlist.id}`,
				label: playlist.name,
				kind: 'Playlist',
				href: `/playlists/${playlist.id}`
			}))
	);

	commands.push(...PLACES.filter((place) => matches(place, needle)));

	if (!looksLikeUrl(trimmed)) {
		commands.push({
			id: 'search-everything',
			label: `Search for "${trimmed}"`,
			kind: 'Search',
			href: `/search?q=${encodeURIComponent(trimmed)}`
		});
	}

	return commands;
}

function matches(command: Command, needle: string): boolean {
	return (
		command.label.toLowerCase().includes(needle) ||
		(command.keywords?.includes(needle) ?? false)
	);
}

function rank(name: string, needle: string): number {
	return name.toLowerCase().startsWith(needle) ? 0 : 1;
}
