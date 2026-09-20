/**
 * The words the app uses for the things it keeps saying.
 *
 * One state had four names: a link was "Watched" in a playlist, "Unwatched" in a filter,
 * "finished" in the reader and "Done" in the queue — and "watched" is an odd thing to say about
 * a paper, a repository or a recipe. "Done" covers all of them and reads the same everywhere.
 * The API's enum is still `Watched`; this is only what a person sees.
 */

/** What the status filter offers, in the order it offers it. */
export const STATUS_FILTERS = ['All', 'Unwatched', 'Watched'] as const;

export type StatusFilter = (typeof STATUS_FILTERS)[number];

export const STATUS_LABELS: Record<StatusFilter, string> = {
	All: 'All',
	Unwatched: 'To do',
	Watched: 'Done'
};

/** What the button that changes the state says, given where the link is now. */
export function doneToggleLabel(done: boolean): string {
	return done ? 'Mark as not done' : 'Mark done';
}

/** The word for the state itself, on a row that is in it. */
export const DONE_LABEL = 'Done';

/**
 * A count with the right noun.
 *
 * "3 items" on one screen and "3 links" on the next are the same three things; saying it two
 * ways makes a reader check whether they are.
 */
export function plural(count: number, one: string, many = `${one}s`): string {
	return `${count} ${count === 1 ? one : many}`;
}
