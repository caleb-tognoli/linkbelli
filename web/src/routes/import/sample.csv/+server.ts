import type { RequestHandler } from './$types';

/**
 * A file to copy the shape of.
 *
 * "Expected format: url,note" is clear enough to somebody who already knows what a CSV is, and
 * useless to everybody else — this is the same sentence as a file they can open in a spreadsheet
 * and type over.
 */
const SAMPLE = `url,note
https://example.com/an-article,Why this one is worth keeping
https://example.org/another,
`;

export const GET: RequestHandler = () =>
	new Response(SAMPLE, {
		headers: {
			'content-type': 'text/csv; charset=utf-8',
			'content-disposition': 'attachment; filename="linkbelli-sample.csv"',
			'cache-control': 'public, max-age=3600'
		}
	});
