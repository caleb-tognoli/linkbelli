import type { Usage } from '$lib/types';

export interface OnboardingStep {
	id: 'playlist' | 'links' | 'source' | 'publish';
	title: string;
	body: string;
	href: string;
	cta: string;
	done: boolean;
	/**
	 * Whether not doing this keeps the checklist on screen.
	 *
	 * Only the first two are required. Plenty of people have no use for a source and no wish to
	 * publish anything, and a box they can never tick is a permanent reproach rather than help.
	 */
	required: boolean;
}

/**
 * What a new account still has ahead of it.
 *
 * Read from what the account actually contains rather than from a stored checklist, so it cannot
 * disagree with reality — no step can be ticked by a flag that got set when the thing itself
 * failed, and none can stay unticked after the person has plainly done it.
 */
export function onboardingSteps(usage: Usage | null): OnboardingStep[] {
	const playlists = usage?.playlists ?? 0;
	// Links still being fetched count: they have been added, which is what the step asked for.
	const links = (usage?.items ?? 0) + (usage?.pendingItems ?? 0);
	const sources = usage?.sources ?? 0;
	const published = usage?.published ?? 0;

	return [
		{
			id: 'playlist',
			title: 'Make a playlist',
			body: 'One list per subject — a reading queue, a watch list, a trail of research.',
			href: '/playlists',
			cta: 'New playlist',
			done: playlists > 0,
			required: true
		},
		{
			id: 'links',
			title: 'Put some links in it',
			body: 'Paste a pile of URLs, bring in a browser bookmark export, or grab the bookmarklet and save pages as you read them.',
			href: '/import',
			cta: 'Import links',
			done: links > 0,
			required: true
		},
		{
			id: 'source',
			title: 'Let it fill itself',
			body: 'Point a source at a feed or a page and new links arrive on their own, on a schedule you set.',
			href: '/sources',
			cta: 'Add a source',
			done: sources > 0,
			required: false
		},
		{
			id: 'publish',
			title: 'Share one, or keep them all',
			body: 'A playlist can be published under your name, handed out as an unlisted link, or stay entirely yours. Private is the default and nothing changes that on your behalf.',
			href: '/playlists',
			cta: 'See your playlists',
			done: published > 0,
			required: false
		}
	];
}

/**
 * Whether the checklist is still worth the space.
 *
 * It goes away on its own once the account has a playlist with something in it — the point at
 * which a person is plainly using the app and does not need to be told how. Dismissing is for
 * everyone else.
 */
export function shouldShowOnboarding(usage: Usage | null, dismissed: boolean): boolean {
	if (dismissed) return false;

	return onboardingSteps(usage).some((step) => step.required && !step.done);
}

/** How far along, counting only the steps that hold the checklist open. */
export function onboardingProgress(steps: OnboardingStep[]): { done: number; total: number } {
	const required = steps.filter((s) => s.required);

	return { done: required.filter((s) => s.done).length, total: required.length };
}

/** The step to lead with: the first unfinished one, required or not. */
export function nextStep(steps: OnboardingStep[]): OnboardingStep | null {
	return steps.find((s) => !s.done) ?? null;
}
