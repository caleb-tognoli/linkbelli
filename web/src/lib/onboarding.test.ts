import { describe, expect, it } from 'vitest';
import {
	nextStep,
	onboardingProgress,
	onboardingSteps,
	shouldShowOnboarding
} from './onboarding';
import type { Usage } from './types';

const usage = (over: Partial<Usage> = {}): Usage =>
	({
		playlists: 0,
		items: 0,
		pendingItems: 0,
		folders: 0,
		sources: 0,
		savedSearches: 0,
		sites: 0,
		watched: 0,
		broken: 0,
		inTrash: 0,
		published: 0,
		...over
	}) as Usage;

const byId = (u: Usage | null, id: string) => onboardingSteps(u).find((s) => s.id === id)!;

describe('onboardingSteps', () => {
	it('starts with nothing done', () => {
		expect(onboardingSteps(usage()).every((s) => !s.done)).toBe(true);
	});

	it('ticks each step from what the account actually holds', () => {
		expect(byId(usage({ playlists: 1 }), 'playlist').done).toBe(true);
		expect(byId(usage({ items: 5 }), 'links').done).toBe(true);
		expect(byId(usage({ sources: 1 }), 'source').done).toBe(true);
		expect(byId(usage({ published: 1 }), 'publish').done).toBe(true);
	});

	it('counts links that are still being fetched', () => {
		// They have been added, which is what the step asked for; waiting on the fetch is ours.
		expect(byId(usage({ items: 0, pendingItems: 3 }), 'links').done).toBe(true);
	});

	it('treats a missing usage response as an account with nothing in it', () => {
		expect(onboardingSteps(null).every((s) => !s.done)).toBe(true);
	});

	it('requires only the two steps without which nothing works', () => {
		const required = onboardingSteps(usage())
			.filter((s) => s.required)
			.map((s) => s.id);

		// A source and a published playlist are both things people legitimately never want.
		expect(required).toEqual(['playlist', 'links']);
	});
});

describe('shouldShowOnboarding', () => {
	it('shows to an account with nothing in it', () => {
		expect(shouldShowOnboarding(usage(), false)).toBe(true);
	});

	it('goes away once there is a playlist with something in it', () => {
		expect(shouldShowOnboarding(usage({ playlists: 1, items: 1 }), false)).toBe(false);
	});

	it('stays while the account has a playlist but nothing in it', () => {
		expect(shouldShowOnboarding(usage({ playlists: 1 }), false)).toBe(true);
	});

	it('does not hang around waiting for a source nobody wants', () => {
		// The optional steps must never be what keeps it on screen.
		expect(shouldShowOnboarding(usage({ playlists: 2, items: 40, sources: 0, published: 0 }), false))
			.toBe(false);
	});

	it('stays away once dismissed, however empty the account is', () => {
		expect(shouldShowOnboarding(usage(), true)).toBe(false);
	});
});

describe('onboardingProgress', () => {
	it('counts only the steps that hold it open', () => {
		// Two of four steps are optional, so a full bar must not need all four.
		expect(onboardingProgress(onboardingSteps(usage()))).toEqual({ done: 0, total: 2 });
		expect(onboardingProgress(onboardingSteps(usage({ playlists: 1 })))).toEqual({
			done: 1,
			total: 2
		});
		expect(onboardingProgress(onboardingSteps(usage({ playlists: 1, items: 9 })))).toEqual({
			done: 2,
			total: 2
		});
	});
});

describe('nextStep', () => {
	it('points at the first thing left to do', () => {
		expect(nextStep(onboardingSteps(usage()))?.id).toBe('playlist');
		expect(nextStep(onboardingSteps(usage({ playlists: 1 })))?.id).toBe('links');
		expect(nextStep(onboardingSteps(usage({ playlists: 1, items: 2 })))?.id).toBe('source');
	});

	it('has nothing to point at once everything is done', () => {
		expect(nextStep(onboardingSteps(usage({ playlists: 1, items: 1, sources: 1, published: 1 }))))
			.toBeNull();
	});
});
