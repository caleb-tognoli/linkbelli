import { render, screen, waitFor } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '$lib/testing/fakeApi';
import TemplatePicker from './TemplatePicker.svelte';
import type { Playlist } from '$lib/types';

vi.mock('$app/navigation', () => ({ goto: vi.fn() }));

/**
 * The template chooser is the first thing on "New source". When there is nothing to choose
 * from it has to get out of the way — it used to call back into its parent from the markup,
 * which Svelte refuses, and the page stayed on "Loading templates…" with no way forward.
 */

afterEach(() => {
	vi.unstubAllGlobals();
});

const PLAYLISTS = [
	{ id: 'p1', name: 'Canals', visibility: 'Private', tags: [], itemCount: 0 }
] as unknown as Playlist[];

describe('TemplatePicker', () => {
	it('hands over to the form by hand when no templates are seeded', async () => {
		fakeApi({ 'GET /sources/templates': json([]) });
		const onskip = vi.fn();
		render(TemplatePicker, { onskip });

		await waitFor(() => expect(onskip).toHaveBeenCalledOnce());
		expect(screen.queryByText('Loading templates…')).toBeNull();
	});

	it('hands over to the form by hand when the templates cannot be fetched', async () => {
		fakeApi({ 'GET /sources/templates': new Response('boom', { status: 500 }) });
		const onskip = vi.fn();
		render(TemplatePicker, { onskip });

		await waitFor(() => expect(onskip).toHaveBeenCalledOnce());
	});

	it('attaches the new source to the playlist it was told to fill', async () => {
		const { calls } = fakeApi({
			'GET /sources/templates': json([
				{
					id: 't1',
					name: 'Subreddit',
					description: 'New posts in a subreddit.',
					fields: [{ key: 'subreddit', label: 'Subreddit', required: true }]
				}
			]),
			'POST /sources': json({ id: 's9' }, 201)
		});
		render(TemplatePicker, { onskip: vi.fn(), playlists: PLAYLISTS });

		await userEvent.click(await screen.findByRole('button', { name: /Subreddit/ }));
		await userEvent.type(screen.getByRole('textbox', { name: /^Subreddit/ }), 'canals');
		await userEvent.click(screen.getByRole('button', { name: 'Create source' }));

		const created = calls.find((c) => c.method === 'POST' && c.path === '/sources');
		expect(created?.body).toMatchObject({ templateId: 't1', playlistIds: ['p1'] });
	});

	it('offers the templates when there are some', async () => {
		fakeApi({
			'GET /sources/templates': json([
				{ id: 't1', name: 'Subreddit', description: 'New posts in a subreddit.', fields: [] }
			])
		});
		const onskip = vi.fn();
		render(TemplatePicker, { onskip });

		expect(await screen.findByRole('button', { name: /Subreddit/ })).toBeTruthy();
		expect(onskip).not.toHaveBeenCalled();
	});
});
