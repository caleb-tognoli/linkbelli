import { describe, expect, it } from 'vitest';
import { activeFilterCount, apiQuery, EMPTY_FILTERS, filtersFrom, pageQuery, resultsPath } from './searchParams';

describe('search filters', () => {
	it('round-trips every filter through the page URL, tags included', () => {
		const url = new URLSearchParams(
			'q=canal&host=bbc.co.uk&status=unwatched&finished=7&broken=1&sort=score&kind=video&maxMinutes=5&itemTag=rust&itemTag=zig'
		);
		const filters = filtersFrom(url);
		expect(pageQuery(filters).toString()).toBe(url.toString());
	});

	it('asks the API for every filter, so a later page stays filtered', () => {
		const filters = filtersFrom(new URLSearchParams('q=canal&kind=video&maxMinutes=5&itemTag=rust'));
		const query = apiQuery(filters, { cursor: 'abc', limit: 25 });
		expect(query.get('kind')).toBe('video');
		expect(query.get('maxMinutes')).toBe('5');
		expect(query.getAll('itemTag')).toEqual(['rust']);
		expect(query.get('cursor')).toBe('abc');
	});

	it('turns a day count into an instant', () => {
		const now = Date.UTC(2026, 8, 19);
		const query = apiQuery({ ...EMPTY_FILTERS, finished: '7' }, { now });
		expect(query.get('finishedSince')).toBe(new Date(now - 7 * 86_400_000).toISOString());
	});

	it('pages a saved search by id rather than by its (empty) boxes', () => {
		expect(resultsPath(EMPTY_FILTERS, 's1', { cursor: 'next' })).toBe('/search/saved/s1?limit=25&cursor=next');
	});

	it('counts tags and every other filter as narrowing the results', () => {
		expect(activeFilterCount(EMPTY_FILTERS)).toBe(0);
		expect(activeFilterCount({ ...EMPTY_FILTERS, kind: 'video' })).toBe(1);
		expect(activeFilterCount({ ...EMPTY_FILTERS, itemTags: ['a', 'b'], finished: '7' })).toBe(3);
	});
});
