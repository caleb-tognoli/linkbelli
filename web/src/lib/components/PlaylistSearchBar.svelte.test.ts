import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '$lib/testing/fakeApi';
import PlaylistSearchBar from './PlaylistSearchBar.svelte';

/**
 * The one box that both filters a playlist and adds to it.
 *
 * Two jobs in one control is where it went wrong before: the add button used to vanish whenever
 * a pasted address happened to match something already in the list, with nothing to say why, and
 * Enter pressed straight after a paste did nothing because the count had not caught up.
 */

const URL = 'https://example.org/a-good-piece';

function setup(props: { isOwner?: boolean; resultCount?: number } = {}) {
	const onAdded = vi.fn();
	render(PlaylistSearchBar, {
		playlistId: 'p1',
		isOwner: props.isOwner ?? true,
		query: '',
		resultCount: props.resultCount ?? 0,
		onAdded
	});
	return { onAdded, box: screen.getByRole('textbox', { name: 'Search or add link' }) };
}

afterEach(() => {
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('PlaylistSearchBar', () => {
	it('only filters when what is typed is not an address', async () => {
		const { calls } = fakeApi({});
		const { box } = setup();

		await userEvent.type(box, 'canals');

		expect(screen.queryByRole('button', { name: /add/i })).not.toBeInTheDocument();
		expect(calls).toEqual([]);
	});

	it('offers to add a pasted address, and adds it on Enter', async () => {
		const item = { id: 'i1', link: { url: URL } };
		const { calls } = fakeApi({ 'POST /playlists/p1/items': json(item, 201) });
		const { box, onAdded } = setup();

		await userEvent.click(box);
		await userEvent.paste(URL);
		expect(screen.getByRole('button', { name: 'Add link' })).toBeInTheDocument();

		await userEvent.keyboard('{Enter}');

		expect(calls).toContainEqual({ method: 'POST', path: '/playlists/p1/items', body: { url: URL } });
		expect(onAdded).toHaveBeenCalledWith(item);
		// Cleared, so the list stops being filtered down to the thing just added.
		expect(box).toHaveValue('');
	});

	/**
	 * The regression this component is known for: the button used to disappear whenever the
	 * address matched something in the list. It stays, and says what it will do.
	 */
	it('still offers to add an address that matches something already listed', async () => {
		fakeApi({});
		const { box } = setup({ resultCount: 1 });

		await userEvent.click(box);
		await userEvent.paste(URL);

		expect(screen.getByRole('button', { name: 'Add it anyway' })).toBeInTheDocument();
		expect(screen.getByText(/already in this playlist — it is in the list below/)).toBeInTheDocument();
	});

	it('says plainly when the link was already there, rather than calling it a failure', async () => {
		fakeApi({ 'POST /playlists/p1/items': json({ title: 'Conflict' }, 409) });
		const { box, onAdded } = setup();

		await userEvent.click(box);
		await userEvent.paste(URL);
		await userEvent.click(screen.getByRole('button', { name: 'Add link' }));

		expect(screen.getByRole('status')).toHaveTextContent('That link is already in this playlist.');
		expect(screen.queryByRole('alert')).not.toBeInTheDocument();
		expect(onAdded).not.toHaveBeenCalled();
		expect(box).toHaveValue('');
	});

	/** A failed save is said out loud, and what was typed is kept so it can be tried again. */
	it('shows an error and keeps the address when adding fails', async () => {
		fakeApi({ 'POST /playlists/p1/items': json({}, 500) });
		const { box, onAdded } = setup();

		await userEvent.click(box);
		await userEvent.paste(URL);
		await userEvent.click(screen.getByRole('button', { name: 'Add link' }));

		expect(screen.getByRole('alert')).toHaveTextContent('Could not add the link.');
		expect(box).toHaveValue(URL);
		expect(onAdded).not.toHaveBeenCalled();
	});

	it('tells a lost connection apart from a refusal', async () => {
		fakeApi({ 'POST /playlists/p1/items': new TypeError('Failed to fetch') });
		const { box } = setup();

		await userEvent.click(box);
		await userEvent.paste(URL);
		await userEvent.click(screen.getByRole('button', { name: 'Add link' }));

		expect(screen.getByRole('alert')).toHaveTextContent('Could not reach the server.');
		expect(box).toHaveValue(URL);
	});

	it('previews an address once typing has paused', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		const { calls } = fakeApi({
			'POST /links/preview': json({
				canonicalUrl: URL,
				host: 'example.org',
				title: 'A good piece',
				description: 'About canals.',
				imageUrl: null,
				siteName: null
			})
		});
		const { box } = setup();
		const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

		await user.click(box);
		await user.paste(URL);
		expect(calls).toEqual([]);

		await vi.advanceTimersByTimeAsync(800);

		expect(await screen.findByText('A good piece')).toBeInTheDocument();
		expect(calls.filter((c) => c.path === '/links/preview')).toHaveLength(1);
	});

	/** The preview takes a moment; pressing Enter in that moment used to do nothing at all. */
	it('adds on Enter while the preview is still on its way', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		let answerPreview: (r: Response) => void = () => {};
		const { calls } = fakeApi({
			'POST /links/preview': () => new Promise<Response>((resolve) => (answerPreview = resolve)),
			'POST /playlists/p1/items': json({ id: 'i1', link: { id: 'l1', url: URL } }, 201)
		});
		const { box, onAdded } = setup();
		const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

		await user.click(box);
		await user.paste(URL);
		await vi.advanceTimersByTimeAsync(800);
		expect(calls.filter((c) => c.path === '/links/preview')).toHaveLength(1);

		await user.keyboard('{Enter}');

		expect(onAdded).toHaveBeenCalledOnce();
		expect(calls.filter((c) => c.path === '/playlists/p1/items')).toHaveLength(1);
		answerPreview(json({ canonicalUrl: URL, host: 'example.org', title: 'Late', description: null, imageUrl: null, siteName: null }));
	});

	/** Somebody reading a shared playlist can search it, but it is not theirs to add to. */
	it('never offers adding to somebody who does not own the playlist', async () => {
		const { calls } = fakeApi({});
		const { box } = setup({ isOwner: false });

		await userEvent.click(box);
		await userEvent.paste(URL);
		await userEvent.keyboard('{Enter}');

		expect(box).toHaveAttribute('placeholder', 'Search…');
		expect(screen.queryByRole('button', { name: /add/i })).not.toBeInTheDocument();
		expect(calls).toEqual([]);
	});
});
