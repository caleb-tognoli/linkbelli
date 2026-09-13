import type { AutomationRule } from '$lib/types';

/**
 * A rule in a sentence.
 *
 * A row of raw conditions and destination ids tells you nothing about what a rule will do, and
 * "what will this actually do" is the only question anyone has when looking at a list of them.
 */
export function describeRule(
	rule: AutomationRule,
	playlistName: (id: string | null) => string
): string {
	const when: string[] = [];
	if (rule.playlistId) when.push(`it lands in ${playlistName(rule.playlistId)}`);
	if (rule.kind) when.push(`it is ${article(rule.kind)}`);
	if (rule.host) when.push(`it is from ${rule.host}`);
	if (rule.titlePattern) when.push(`its title matches /${rule.titlePattern}/`);
	if (rule.urlPattern) when.push(`its address matches /${rule.urlPattern}/`);
	if (rule.broken === true) when.push('its page has gone');
	if (rule.broken === false) when.push('its page is still there');

	// One clause for a range rather than two, because "it takes at least 5 minutes and it takes
	// at most 20 minutes" is how a form would say it, not how a person would.
	const length = describeLength(rule.minMinutes, rule.maxMinutes);
	if (length) when.push(length);

	const then: string[] = [];
	if (rule.addTags.length) then.push(`tag it ${rule.addTags.join(', ')}`);
	if (rule.moveToPlaylistId) then.push(`move it to ${playlistName(rule.moveToPlaylistId)}`);
	if (rule.copyToPlaylistId) then.push(`also put it in ${playlistName(rule.copyToPlaylistId)}`);
	if (rule.markWatched) then.push('mark it watched');
	if (rule.setScore !== null && rule.setScore !== undefined) then.push(`score it ${rule.setScore}`);
	if (rule.archive) then.push('keep a public snapshot of it');
	if (rule.trash) then.push('send it to the trash');

	// A rule that matches but does nothing is worth saying plainly rather than rendering as an
	// empty half-sentence.
	const action = then.length ? join(then) : 'do nothing';
	const condition = when.length ? join(when) : 'anything arrives';

	return capitalize(`when ${condition}, ${action}.${rule.stopOnMatch ? ' Then stop.' : ''}`);
}

/** "it takes over 20 minutes", "it reads in 5 to 10 minutes" — never two clauses for one range. */
function describeLength(min: number | null | undefined, max: number | null | undefined): string | null {
	if (min && max) return `it reads in ${min} to ${max} minutes`;
	if (min) return `it takes over ${min} minutes`;
	if (max) return `it reads in under ${max} minutes`;

	return null;
}

/** "an article", "a video" — small, but it is the difference between English and a data dump. */
function article(kind: string): string {
	const word = kind.toLowerCase();
	return `${'aeiou'.includes(word[0]) ? 'an' : 'a'} ${word}`;
}

function join(parts: string[]): string {
	if (parts.length === 1) return parts[0];

	return `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`;
}

function capitalize(text: string): string {
	return text.charAt(0).toUpperCase() + text.slice(1);
}
