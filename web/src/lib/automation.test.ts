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
		maxMinutes: null,
		minMinutes: null,
		broken: null,
		sourceId: null,
		addTags: [],
		moveToPlaylistId: null,
		copyToPlaylistId: null,
		markWatched: false,
		setScore: null,
		archive: false,
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
			'When anything arrives, mark it done.'
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

	/** The conditions and actions a rule gained, said in the same register as the rest. */
	it('says a length range as one clause, not two', () => {
		expect(describeRule(rule({ minMinutes: 20 }), name)).toContain('it takes over 20 minutes');
		expect(describeRule(rule({ maxMinutes: 5 }), name)).toContain('it reads in under 5 minutes');
		expect(describeRule(rule({ minMinutes: 5, maxMinutes: 10 }), name)).toContain(
			'it reads in 5 to 10 minutes'
		);
	});

	it('says which way round a broken condition points', () => {
		expect(describeRule(rule({ broken: true }), name)).toContain('its page has gone');
		expect(describeRule(rule({ broken: false }), name)).toContain('its page is still there');
		expect(describeRule(rule({ broken: null }), name)).not.toContain('page');
	});

	it('says the new actions', () => {
		expect(describeRule(rule({ setScore: 80 }), name)).toContain('score it 80');
		expect(describeRule(rule({ archive: true }), name)).toContain('keep a public snapshot');
	});

	/** Zero is a real score, and the falsy check that would drop it is the obvious mistake. */
	it('keeps a score of zero', () => {
		expect(describeRule(rule({ setScore: 0 }), name)).toContain('score it 0');
	});
});
