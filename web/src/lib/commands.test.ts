import { describe, expect, it } from 'vitest';
import { PLACES, buildCommands } from './commands';
import type { Playlist } from './types';

function playlist(id: string, name: string): Playlist {
	return { id, name, slug: name.toLowerCase(), itemCount: 0, tags: [] } as unknown as Playlist;
}

const playlists = [
	playlist('1', 'Reading'),
	playlist('2', 'Weekend reading'),
	playlist('3', 'Watching')
];

describe('buildCommands', () => {
	it('offers the places to go when nothing is typed', () => {
		expect(buildCommands('', playlists)).toEqual(PLACES);
		expect(buildCommands('   ', playlists)).toEqual(PLACES);
	});

	it('finds playlists by name', () => {
		const labels = buildCommands('read', playlists)
			.filter((c) => c.kind === 'Playlist')
			.map((c) => c.label);

		expect(labels).toEqual(['Reading', 'Weekend reading']);
	});

	it('puts a name that starts with the query above one that merely contains it', () => {
		const labels = buildCommands('reading', playlists)
			.filter((c) => c.kind === 'Playlist')
			.map((c) => c.label);

		expect(labels[0]).toBe('Reading');
	});

	it('always offers to search everything', () => {
		const commands = buildCommands('postgres', playlists);
		const search = commands.find((c) => c.kind === 'Search');

		expect(search?.href).toBe('/search?q=postgres');
	});

	it('leads with saving when the query is an address', () => {
		// Nobody types https:// into a palette hoping to navigate.
		const commands = buildCommands('https://example.com/a', playlists);

		expect(commands[0].kind).toBe('Save');
		expect(commands[0].href).toBe(`/save?url=${encodeURIComponent('https://example.com/a')}`);
	});

	it('does not offer to search for an address', () => {
		const commands = buildCommands('https://example.com/a', playlists);

		expect(commands.some((c) => c.kind === 'Search')).toBe(false);
	});

	it('matches a destination on its keywords, not just its label', () => {
		const commands = buildCommands('deleted', playlists);

		expect(commands.some((c) => c.href === '/trash')).toBe(true);
	});

	it('is case insensitive', () => {
		expect(buildCommands('WATCHING', playlists).some((c) => c.label === 'Watching')).toBe(true);
	});

	it('caps how many playlists it offers, so the list stays scannable', () => {
		const many = Array.from({ length: 20 }, (_, n) => playlist(String(n), `Reading ${n}`));

		expect(buildCommands('reading', many).filter((c) => c.kind === 'Playlist')).toHaveLength(6);
	});

	it('still offers a search when nothing else matches', () => {
		const commands = buildCommands('nothing matches this', playlists);

		expect(commands).toHaveLength(1);
		expect(commands[0].kind).toBe('Search');
	});
});
