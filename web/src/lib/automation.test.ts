import { describe, expect, it } from 'vitest';
import { describeRule } from './automation';
import type { AutomationRule } from './types';

const names: Record<string, string> = { inbox: 'Inbox', later: 'Later', digest: 'Digest' };

function name(id: string | null): string {
	return (id && names[id]) || 'a playlist';
}

function rule(overrides: Partial<AutomationRule> = {}): AutomationRule {
	return {
		id: 'r1',
		name: 'A rule',
		enabled: true,
		position: 1,
		playlistId: null,
		host: null,
		titlePattern: null,
		urlPattern: null,
		kind: null,
		addTags: [],
		moveToPlaylistId: null,
		copyToPlaylistId: null,
		markWatched: false,
		trash: false,
		stopOnMatch: false,
		matchCount: 0,
		lastMatchedAt: null,
		creationTime: '2026-09-11T00:00:00Z',
		...overrides
	};
}

describe('describeRule', () => {
	it('reads as a sentence rather than a row of fields', () => {
		const text = describeRule(
			rule({ host: 'example.com', addTags: ['rust'], moveToPlaylistId: 'later' }),
			name
		);

		expect(text).toBe('When it is from example.com, tag it rust and move it to Later.');
	});

	it('names the playlists rather than their ids', () => {
		const text = describeRule(rule({ playlistId: 'inbox', copyToPlaylistId: 'digest' }), name);

		expect(text).toContain('it lands in Inbox');
		expect(text).toContain('also put it in Digest');
		expect(text).not.toContain('inbox');
	});

	it('says plainly that a rule with no conditions catches everything', () => {
		expect(describeRule(rule({ markWatched: true }), name)).toBe(
			'When anything arrives, mark it watched.'
		);
	});

	it('says plainly that a rule with no actions does nothing', () => {
		// An empty half-sentence would read as a rendering bug rather than as a useless rule.
		expect(describeRule(rule({ host: 'example.com' }), name)).toBe(
			'When it is from example.com, do nothing.'
		);
	});

	it('joins several conditions and several actions', () => {
		const text = describeRule(
			rule({ host: 'example.com', titlePattern: 'rust', addTags: ['a', 'b'], trash: true }),
			name
		);

		expect(text).toBe(
			'When it is from example.com and its title matches /rust/, tag it a, b and send it to the trash.'
		);
	});

	it('mentions stopping, because it changes what the rules below it see', () => {
		expect(describeRule(rule({ markWatched: true, stopOnMatch: true }), name)).toContain('Then stop.');
	});

	it('gets the article right in front of a vowel', () => {
		expect(describeRule(rule({ kind: 'Article' }), name)).toContain('it is an article');
		expect(describeRule(rule({ kind: 'Video' }), name)).toContain('it is a video');
	});
});
