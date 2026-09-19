import { render, screen, within } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '$lib/testing/fakeApi';
import type { PlaylistItem } from '$lib/types';
import LinkTable from './LinkTable.svelte';
import Toaster from './Toaster.svelte';
import { toast } from '$lib/toast.svelte';

// The confirmation for a bulk delete is drawn by a dialog mounted once in the root layout, which
// these tests do not render. Answered here instead, per test.
const confirmDialog = vi.hoisted(() => vi.fn<(message: string) => Promise<boolean>>());
vi.mock('$lib/dialog.svelte', () => ({ confirmDialog, promptDialog: vi.fn() }));

/**
 * The table a playlist is read and worked through in: 1,300 lines and the most-used screen in
 * the app, with nothing testing it. These are about the part that changes things — selecting,
 * acting on a selection, and saying so when a change did not happen. A write that fails quietly
 * looks exactly like one that worked and a page that did not refresh.
 */

function item(n: number, over: Partial<PlaylistItem> = {}): PlaylistItem {
	return {
		id: `item-${n}`,
		position: n * 1024,
		note: null,
		status: 'Added',
		link: {
			id: `link-${n}`,
			url: `https://example.org/${n}`,
			host: 'example.org',
			title: `Article ${n}`,
			description: null,
			thumbnailUrl: null,
			siteName: 'Example',
			enriched: true,
			nsfw: false,
			favicon: null,
			enrichmentStatus: 'Succeeded',
			enrichmentError: null
		},
		creationTime: '2026-09-01T10:00:00Z',
		metadata: null,
		sourceId: null,
		score: null,
		...over
	} as PlaylistItem;
}

function setup(count = 3) {
	// The table reports through the app's toasts, which the root layout draws.
	render(Toaster);
	const onmove = vi.fn(async () => {});
	render(LinkTable, { items: Array.from({ length: count }, (_, n) => item(n + 1)), playlistId: 'p1', onmove });
	return { onmove };
}

const select = (n: number) => userEvent.click(screen.getByRole('checkbox', { name: `Select Article ${n}` }));

/**
 * The table's own error, as opposed to the drag library's: svelte-dnd-action keeps a
 * screen-reader alert region of its own on the page, so there are two alerts on screen in the app
 * as well, and only one of them is this component's.
 */
function problem(): HTMLElement {
	const own = screen.getAllByRole('alert').filter((el) => el.id !== 'dnd-action-aria-alert');
	expect(own).toHaveLength(1);
	return own[0];
}

beforeEach(() => {
	confirmDialog.mockReset();
});

afterEach(() => {
	vi.unstubAllGlobals();
	toast.clear();
});

describe('LinkTable — selecting', () => {
	it('shows what can be done to a selection only once something is selected', async () => {
		fakeApi({});
		setup();

		expect(screen.queryByText(/selected$/)).not.toBeInTheDocument();

		await select(1);
		await select(3);

		expect(screen.getByText('2 selected')).toBeInTheDocument();
	});

	it('selects everything, and clears it again', async () => {
		fakeApi({});
		setup(4);

		await userEvent.click(screen.getByRole('checkbox', { name: 'Select all' }));
		expect(screen.getByText('4 selected')).toBeInTheDocument();

		await userEvent.click(screen.getByRole('button', { name: 'Clear selection' }));
		expect(screen.queryByText(/selected$/)).not.toBeInTheDocument();
	});
});

describe('LinkTable — acting on a selection', () => {
	it('marks the selection watched in one request, then lets the page refresh', async () => {
		const { calls } = fakeApi({ 'POST /items/bulk': json({ affected: 2, skipped: 0 }) });
		const { onmove } = setup();

		await select(1);
		await select(2);
		await userEvent.click(screen.getByRole('button', { name: 'Watched' }));

		expect(calls).toContainEqual({
			method: 'POST',
			path: '/items/bulk',
			body: { itemIds: ['item-1', 'item-2'], action: 'SetStatus', status: 'Watched' }
		});
		expect(onmove).toHaveBeenCalledOnce();
		expect(screen.queryByText(/selected$/)).not.toBeInTheDocument();
	});

	/**
	 * Forty items not moving looks exactly like forty items moving and the page not refreshing.
	 * A conflict has a different next step from a rate limit, so each is named.
	 */
	it.each([
		[409, 'Somebody changed that first. Reload and try again.'],
		[429, 'Too many changes at once. Try again in a moment.'],
		[403, 'You do not have access to do that any more.'],
		[500, 'Could not do that to the selection.']
	])('says why when the server refuses (%i), and keeps the selection', async (status, message) => {
		fakeApi({ 'POST /items/bulk': json({}, status) });
		const { onmove } = setup();

		await select(1);
		await userEvent.click(screen.getByRole('button', { name: 'Unwatched' }));

		expect(problem()).toHaveTextContent(message);
		expect(screen.getByText('1 selected')).toBeInTheDocument();
		expect(onmove).not.toHaveBeenCalled();
	});

	it('says so when the server cannot be reached at all', async () => {
		fakeApi({ 'POST /items/bulk': new TypeError('Failed to fetch') });
		setup();

		await select(2);
		await userEvent.click(screen.getByRole('button', { name: 'Watched' }));

		expect(problem()).toHaveTextContent('Could not reach the server.');
	});

	it('asks before deleting a selection, and does nothing if told no', async () => {
		const { calls } = fakeApi({ 'POST /items/bulk': json({ affected: 2, skipped: 0 }) });
		confirmDialog.mockResolvedValue(false);
		setup();

		await select(1);
		await select(2);
		await userEvent.click(screen.getByRole('button', { name: 'Delete' }));

		expect(confirmDialog).toHaveBeenCalledWith(
			'Delete 2 links? You can put them back from the trash.',
			expect.objectContaining({ danger: true })
		);
		expect(calls).toEqual([]);
		expect(screen.getByText('2 selected')).toBeInTheDocument();
	});

	it('deletes the selection once confirmed', async () => {
		const { calls } = fakeApi({ 'POST /items/bulk': json({ affected: 1, skipped: 0 }) });
		confirmDialog.mockResolvedValue(true);
		const { onmove } = setup();

		await select(3);
		await userEvent.click(screen.getByRole('button', { name: 'Delete' }));

		expect(calls).toContainEqual({
			method: 'POST',
			path: '/items/bulk',
			body: { itemIds: ['item-3'], action: 'Delete' }
		});
		expect(onmove).toHaveBeenCalledOnce();
	});

	/** The links went to the trash, not away for good, and getting them back is one press. */
	it('offers to undo a delete, and puts the links back', async () => {
		const { calls } = fakeApi({
			'POST /items/bulk': json({ affected: 2, skipped: 0 }),
			'POST /trash/items/item-1/restore': new Response(null, { status: 204 }),
			'POST /trash/items/item-2/restore': new Response(null, { status: 204 })
		});
		confirmDialog.mockResolvedValue(true);
		const { onmove } = setup();

		await select(1);
		await select(2);
		await userEvent.click(screen.getByRole('button', { name: 'Delete' }));
		await userEvent.click(await screen.findByRole('button', { name: 'Undo' }));

		const restored = calls.filter((c) => c.path.startsWith('/trash/items/')).map((c) => c.path);
		expect(restored.sort()).toEqual(['/trash/items/item-1/restore', '/trash/items/item-2/restore']);
		expect(onmove).toHaveBeenCalledTimes(2);
	});

	/** Nothing in flight twice: the buttons stand still while a bulk change is on its way. */
	it('will not start a second change while one is still going', async () => {
		let answer!: (res: Response) => void;
		fakeApi({ 'POST /items/bulk': () => new Promise<Response>((resolve) => (answer = resolve)) });
		setup();

		await select(1);
		await userEvent.click(screen.getByRole('button', { name: 'Watched' }));

		const bar = screen.getByText('1 selected').parentElement!;
		expect(within(bar).getByRole('button', { name: 'Unwatched' })).toBeDisabled();
		expect(within(bar).getByRole('button', { name: 'Delete' })).toBeDisabled();

		answer(json({ affected: 1, skipped: 0 }));
		await vi.waitFor(() => expect(screen.queryByText('1 selected')).not.toBeInTheDocument());
	});
});

describe('LinkTable — moving to another playlist', () => {
	// An open dialog makes the rest of the page inert; one left open by a test would leave the
	// next test's table unclickable.
	afterEach(() => {
		document.body.style.pointerEvents = '';
	});

	const other = { id: 'p2', name: 'Other list', visibility: 'Private', tags: [], itemCount: 0 };

	it('does not say "Moved" when the move was refused', async () => {
		fakeApi({
			'GET /playlists': json({ items: [other], nextCursor: null }),
			'POST /items/bulk': json({}, 409)
		});
		setup();

		await select(1);
		await userEvent.click(screen.getByRole('button', { name: 'Move to…' }));
		await userEvent.click(await screen.findByRole('button', { name: /Other list/ }));

		expect(screen.queryByText('Moved')).not.toBeInTheDocument();
		expect(problem()).toHaveTextContent('Somebody changed that first');
	});

	it('closes the picker once the selection has moved', async () => {
		fakeApi({
			'GET /playlists': json({ items: [other], nextCursor: null }),
			'POST /items/bulk': json({ affected: 1, skipped: 0 })
		});
		const { onmove } = setup();

		await select(1);
		await userEvent.click(screen.getByRole('button', { name: 'Move to…' }));
		await userEvent.click(await screen.findByRole('button', { name: /Other list/ }));

		expect(onmove).toHaveBeenCalled();
		expect(screen.queryByRole('button', { name: /Other list/ })).not.toBeInTheDocument();
	});
});
