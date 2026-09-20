import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '$lib/testing/fakeApi';
import TagEditor from './TagEditor.svelte';

vi.mock('$lib/toast.svelte', () => ({
	toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() }
}));

/**
 * Tagging a playlist.
 *
 * The box used to file whatever was in it the moment focus left — so tabbing away mid-word put
 * "jav" in the library, and there was no way to see what you had typed was not yet a tag.
 */
describe('TagEditor', () => {
	const playlist = (tags: string[]) => json({ id: 'p1', tags });

	it('does not file a half-typed tag when focus leaves', async () => {
		const { calls } = fakeApi({ 'GET /tags': json([]) });
		render(TagEditor, { playlistId: 'p1', tags: [] });

		const box = screen.getByRole('combobox', { name: 'Add tag' });
		await userEvent.type(box, 'rus');
		await userEvent.tab();

		expect(calls.filter((c) => c.method === 'PATCH')).toEqual([]);
		expect(box).toHaveValue('rus');
	});

	it('files it on Enter', async () => {
		const { calls } = fakeApi({ 'GET /tags': json([]), 'PATCH /playlists/p1': playlist(['rust']) });
		render(TagEditor, { playlistId: 'p1', tags: [] });

		await userEvent.type(screen.getByRole('combobox', { name: 'Add tag' }), 'rust{Enter}');

		expect(calls.filter((c) => c.method === 'PATCH')).toEqual([
			{ method: 'PATCH', path: '/playlists/p1', body: { tags: ['rust'] } }
		]);
	});

	it('offers the tags already in use', async () => {
		fakeApi({
			'GET /tags': json([{ name: 'rust', playlistCount: 3 }]),
			'PATCH /playlists/p1': playlist(['rust'])
		});
		render(TagEditor, { playlistId: 'p1', tags: [] });

		await userEvent.type(screen.getByRole('combobox', { name: 'Add tag' }), 'ru');

		expect(await screen.findByRole('option', { name: /rust/ })).toBeInTheDocument();
	});

	it('clears the draft on Escape', async () => {
		const { calls } = fakeApi({ 'GET /tags': json([]) });
		render(TagEditor, { playlistId: 'p1', tags: [] });

		const box = screen.getByRole('combobox', { name: 'Add tag' });
		await userEvent.type(box, 'draft{Escape}');

		expect(box).toHaveValue('');
		expect(calls.filter((c) => c.method === 'PATCH')).toEqual([]);
	});
});
