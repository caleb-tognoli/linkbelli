import { render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '$lib/testing/fakeApi';
import TemplatePicker from './TemplatePicker.svelte';

vi.mock('$app/navigation', () => ({ goto: vi.fn() }));

/**
 * The template chooser is the first thing on "New source". When there is nothing to choose
 * from it has to get out of the way — it used to call back into its parent from the markup,
 * which Svelte refuses, and the page stayed on "Loading templates…" with no way forward.
 */

afterEach(() => {
	vi.unstubAllGlobals();
});

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
