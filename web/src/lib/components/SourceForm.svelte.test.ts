import { render, screen } from '@testing-library/svelte';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fakeApi, json } from '$lib/testing/fakeApi';
import type { Playlist, Source } from '$lib/types';
import SourceForm from './SourceForm.svelte';

const navigation = vi.hoisted(() => ({ goto: vi.fn(), invalidateAll: vi.fn(), beforeNavigate: vi.fn() }));
vi.mock('$app/navigation', () => navigation);

const confirmDialog = vi.hoisted(() => vi.fn<(message: string) => Promise<boolean>>());
vi.mock('$lib/dialog.svelte', () => ({ confirmDialog, promptDialog: vi.fn() }));

/**
 * Making and changing a source: the form where a wrong selector or a bad pattern is found out.
 *
 * The things worth pinning are the ones that used to be discovered too late — that a config
 * matched nothing, that the server rejected a pattern and said which — and that a failure is
 * said out loud rather than left as red text nobody not looking at the screen would notice.
 */

const FEED = 'https://example.org/feed.xml';

/** Somewhere for a new source's links to land, which the form now insists on. */
const PLAYLISTS = [{ id: 'p1', name: 'Canals', visibility: 'Private', tags: [], itemCount: 0 }] as unknown as Playlist[];

function existing(over: Partial<Source> = {}): Source {
	return {
		id: 's1',
		name: 'Example feed',
		type: 'Rss',
		config: { feedUrl: FEED },
		schedule: '0 * * * *',
		visibility: 'Private',
		lastRunAt: null,
		lastRunStatus: null,
		creationTime: '2026-09-01T10:00:00Z',
		playlistIds: [],
		status: 'Active',
		consecutiveFailures: 0,
		timeZone: 'UTC',
		filter: null,
		...over
	} as Source;
}

beforeEach(() => {
	navigation.goto.mockReset();
	navigation.invalidateAll.mockReset();
	confirmDialog.mockReset();
});

afterEach(() => {
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('SourceForm — creating', () => {
	it('creates an RSS source from what was typed and opens it', async () => {
		const { calls } = fakeApi({ 'POST /sources': json(existing({ id: 'new-1' }), 201) });
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });

		await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Canals weekly');
		await userEvent.type(screen.getByRole('textbox', { name: 'Feed URL' }), FEED);
		await userEvent.click(screen.getByRole('button', { name: 'Create' }));

		const created = calls.find((c) => c.method === 'POST' && c.path === '/sources');
		expect(created?.body).toMatchObject({
			name: 'Canals weekly',
			type: 'Rss',
			config: { feedUrl: FEED },
			status: 'Active',
			visibility: 'Private',
			// Attached as it is made: a source that fills nothing is the product's promise minus
			// its point.
			playlistIds: ['p1']
		});
		expect(navigation.goto).toHaveBeenCalledWith('/sources/new-1');
	});

	/** "Could not save" says nothing about which bracket was left open; the server's words do. */
	it('quotes what the server objected to, and announces it', async () => {
		fakeApi({
			'POST /sources': json(
				{ title: 'Validation failed', errors: { titleInclude: ['That is not a valid pattern: unterminated [ at 4.'] } },
				400
			)
		});
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });

		await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Broken');
		await userEvent.type(screen.getByRole('textbox', { name: /Feed URL/ }), 'https://example.com/feed.xml');
		await userEvent.click(screen.getByRole('button', { name: 'Create' }));

		expect(screen.getByRole('alert')).toHaveTextContent('That is not a valid pattern: unterminated [ at 4.');
		expect(navigation.goto).not.toHaveBeenCalled();
	});

	it('says which field is missing rather than sending the request', async () => {
		const { calls } = fakeApi({ 'POST /sources': json({}) });
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });

		await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'No address');
		await userEvent.click(screen.getByRole('button', { name: 'Create' }));

		expect(screen.getByRole('alert')).toHaveTextContent('Feed URL is needed.');
		expect(calls.filter((c) => c.method === 'POST')).toEqual([]);
	});

	it('names the quota when that is what stopped it', async () => {
		fakeApi({ 'POST /sources': json({}, 429) });
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });

		await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Another');
		await userEvent.type(screen.getByRole('textbox', { name: /Feed URL/ }), 'https://example.com/feed.xml');
		await userEvent.click(screen.getByRole('button', { name: 'Create' }));

		expect(screen.getByRole('alert')).toHaveTextContent('You have reached your source quota.');
	});

	it('has nothing to configure for a pushed source, and says where the address will come from', async () => {
		fakeApi({});
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });

		await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Type' }), 'Webhook');

		expect(screen.queryByRole('textbox', { name: 'Feed URL' })).not.toBeInTheDocument();
		expect(screen.getByText(/Save this source and it will be given a URL/)).toBeInTheDocument();
	});
});

describe('SourceForm — the preview', () => {
	/**
	 * Before the preview, the only way to learn whether a config found anything was to save it,
	 * wait for its first run and read the history.
	 */
	it('shows what the source finds once the address is complete', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		const { calls } = fakeApi({
			'POST /sources/preview': json({ count: 2, links: [{ url: 'https://example.org/a', title: 'First post' }, { url: 'https://example.org/b', title: null }] })
		});
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });
		const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

		await user.type(screen.getByRole('textbox', { name: 'Feed URL' }), FEED);
		await vi.advanceTimersByTimeAsync(1600);

		expect(await screen.findByText('First post')).toBeInTheDocument();
		expect(screen.getByText('https://example.org/b')).toBeInTheDocument();
		expect(screen.getByText('2 links')).toBeInTheDocument();
		// One request for the finished address, not one per keystroke.
		expect(calls.filter((c) => c.path === '/sources/preview')).toHaveLength(1);
	});

	/** The failure that looks like success: the fetch worked and matched nothing. */
	it('warns when the source can be read but finds nothing', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		fakeApi({ 'POST /sources/preview': json({ count: 0, links: [] }) });
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });
		const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

		await user.type(screen.getByRole('textbox', { name: 'Feed URL' }), FEED);
		await vi.advanceTimersByTimeAsync(1600);

		expect(await screen.findByText(/Read it, and found nothing/)).toBeInTheDocument();
	});

	it('says so when previews are being rate-limited', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		fakeApi({ 'POST /sources/preview': json({}, 429) });
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });
		const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

		await user.type(screen.getByRole('textbox', { name: 'Feed URL' }), FEED);
		await vi.advanceTimersByTimeAsync(1600);

		expect(await screen.findByRole('alert')).toHaveTextContent('Too many previews just now');
	});

	it('does not preview an address that is still being typed', async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		const { calls } = fakeApi({});
		render(SourceForm, { mode: 'create', playlists: PLAYLISTS });
		const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

		await user.type(screen.getByRole('textbox', { name: 'Feed URL' }), 'https://exam');
		await vi.advanceTimersByTimeAsync(3000);

		expect(calls).toEqual([]);
	});
});

describe('SourceForm — editing', () => {
	it('saves changes in place and refreshes the page', async () => {
		const { calls } = fakeApi({ 'PATCH /sources/s1': json(existing()) });
		render(SourceForm, { mode: 'edit', source: existing() });

		const name = screen.getByRole('textbox', { name: 'Name' });
		expect(name).toHaveValue('Example feed');

		await userEvent.clear(name);
		await userEvent.type(name, 'Renamed feed');
		await userEvent.click(screen.getByRole('button', { name: 'Save' }));

		expect(calls.find((c) => c.method === 'PATCH')?.body).toMatchObject({ name: 'Renamed feed', config: { feedUrl: FEED } });
		expect(navigation.invalidateAll).toHaveBeenCalled();
	});

	/** Other people's playlists follow a shared source; making it private cuts them off. */
	it('asks before making a shared source private, and saves nothing if told no', async () => {
		const { calls } = fakeApi({ 'PATCH /sources/s1': json(existing()) });
		confirmDialog.mockResolvedValue(false);
		render(SourceForm, { mode: 'edit', source: existing({ visibility: 'Shared' }) });

		await userEvent.click(screen.getByRole('button', { name: /^Visibility/ }));
		await userEvent.click(await screen.findByRole('menuitemradio', { name: /^Private/ }));
		// The menu makes the rest of the page inert while it closes; Save is not clickable until
		// it has let go.
		await vi.waitFor(() => expect(document.body.style.pointerEvents).not.toBe('none'));
		await userEvent.click(screen.getByRole('button', { name: 'Save' }));

		expect(confirmDialog).toHaveBeenCalledWith(expect.stringContaining('unsubscribe it from other users'));
		expect(calls.filter((c) => c.method === 'PATCH')).toEqual([]);
	});

	it('puts a source that stopped itself back on its schedule only when switched on', () => {
		fakeApi({});
		render(SourceForm, { mode: 'edit', source: existing({ status: 'Failing', consecutiveFailures: 5 }) });

		expect(screen.getByRole('switch', { name: 'Run on its schedule' })).not.toBeChecked();
		expect(screen.getByText(/Paused — runs only when you trigger one/)).toBeInTheDocument();
	});
});
