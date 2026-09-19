import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MAX_ATTEMPTS, QUEUE_KEY, type QueuedSave } from '$lib/offlineQueue';
import { offlineSaves } from '$lib/offlineSaves.svelte';
import OfflineQueueBanner from './OfflineQueueBanner.svelte';

/**
 * The banner that says links saved without a connection have not arrived yet.
 *
 * It reports something that happened elsewhere — on a share sheet that has since closed — so if
 * it is wrong, there is no other trace. Driven through the real queue in localStorage, the way
 * the save form fills it, rather than through a stand-in.
 */

function queued(over: Partial<QueuedSave> = {}): QueuedSave {
	return {
		id: crypto.randomUUID(),
		playlistId: 'p1',
		playlistName: 'Reading',
		url: `https://example.org/${crypto.randomUUID()}`,
		note: null,
		queuedAt: new Date().toISOString(),
		attempts: 0,
		...over
	};
}

function seed(...saves: QueuedSave[]) {
	localStorage.setItem(QUEUE_KEY, JSON.stringify(saves));
	offlineSaves.load();
}

beforeEach(() => {
	localStorage.clear();
	offlineSaves.load();
});

afterEach(() => {
	vi.unstubAllGlobals();
});

describe('OfflineQueueBanner', () => {
	it('is not there when nothing is waiting', () => {
		render(OfflineQueueBanner);

		expect(screen.queryByRole('status')).not.toBeInTheDocument();
	});

	it('says how many links are waiting and offers to send them', () => {
		seed(queued(), queued());
		render(OfflineQueueBanner);

		expect(screen.getByRole('status')).toHaveTextContent('2 links waiting to be saved.');
		expect(screen.getByRole('button', { name: /send now/i })).toBeEnabled();
	});

	it('sends what is waiting, and goes away once it has', async () => {
		seed(queued({ url: 'https://example.org/on-the-train' }));
		const fetch = vi.fn(async () => new Response('{}', { status: 201 }));
		vi.stubGlobal('fetch', fetch);
		render(OfflineQueueBanner);

		await userEvent.click(screen.getByRole('button', { name: /send now/i }));

		expect(fetch).toHaveBeenCalledOnce();
		const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
		expect(url).toBe('/api/v1/playlists/p1/items');
		expect(JSON.parse(init.body as string).url).toBe('https://example.org/on-the-train');
		expect(screen.queryByRole('status')).not.toBeInTheDocument();
	});

	/** Still offline: nothing was learned, so nothing is thrown away. */
	it('keeps everything when sending fails for want of a connection', async () => {
		seed(queued(), queued());
		vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))));
		render(OfflineQueueBanner);

		await userEvent.click(screen.getByRole('button', { name: /send now/i }));

		expect(screen.getByRole('status')).toHaveTextContent('2 links waiting to be saved.');
	});

	it('says it is sending, and will not start twice, while it is', async () => {
		seed(queued());
		let answer!: (res: Response) => void;
		vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((resolve) => (answer = resolve))));
		render(OfflineQueueBanner);

		await userEvent.click(screen.getByRole('button', { name: /send now/i }));

		const button = screen.getByRole('button', { name: /sending/i });
		expect(button).toBeDisabled();

		answer(new Response('{}', { status: 201 }));
		await vi.waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument());
	});

	/**
	 * "1 could not be saved" without saying which leaves somebody with nothing to act on, so each
	 * one the server refused is named, with a way to let it go.
	 */
	it('names each link the server refused, and lets it be forgotten', async () => {
		seed(queued({ url: 'https://refused.example/one', attempts: MAX_ATTEMPTS, playlistName: 'Later' }));
		render(OfflineQueueBanner);

		expect(screen.getByRole('status')).toHaveTextContent('1 link could not be saved.');
		expect(screen.getByText('https://refused.example/one')).toBeInTheDocument();
		expect(screen.getByText('→ Later')).toBeInTheDocument();
		// Nothing left to send, so there is nothing to offer sending.
		expect(screen.queryByRole('button', { name: /send now/i })).not.toBeInTheDocument();

		await userEvent.click(screen.getByRole('button', { name: 'Forget https://refused.example/one' }));

		expect(screen.queryByRole('status')).not.toBeInTheDocument();
		expect(JSON.parse(localStorage.getItem(QUEUE_KEY) ?? '[]')).toEqual([]);
	});
});
