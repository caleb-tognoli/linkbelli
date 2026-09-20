<script lang="ts">
	import { formatDate, formatExact } from '$lib/dates';
	import { plural } from '$lib/labels';
	import Button from '$lib/components/ui/Button.svelte';
	import { onDestroy } from 'svelte';
	import { failureMessage } from '$lib/api/errors';
	import BackLink from '$lib/components/ui/BackLink.svelte';
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
	import { toast } from '$lib/toast.svelte';
	import Modal from '$lib/components/ui/Modal.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import { goto, invalidateAll } from '$app/navigation';
	import { page as routePage } from '$app/state';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Play, RotateCcw, ChevronRight, Copy, Plus, Unlink, Lock, EyeOff, Globe, LoaderCircle } from '@lucide/svelte';

	const visIcons = { Private: Lock, Unlisted: EyeOff, Public: Globe } as const;
	import SourceForm from '$lib/components/SourceForm.svelte';
	import SourceHealthCard from '$lib/components/SourceHealthCard.svelte';
	import SourceListItem from '$lib/components/SourceListItem.svelte';
	import type { PageData } from './$types';
	import type { Paged, Playlist, Source, SourceRun } from '$lib/types';

	let { data }: { data: PageData } = $props();
	let busy = $state(false);

	const backHref = $derived(routePage.url.searchParams.get('from') ?? '/sources');
	const backLabel = $derived(routePage.url.searchParams.get('fromLabel') ?? 'Sources');

	const pageSize = 10;
	let shownRuns = $state(pageSize);
	let pagedRuns = $derived(data.runs.slice(0, shownRuns));

	// Open from the start when the last run failed: that is the thing to look at.
	let historyOpen = $state(data.runs[0]?.status === 'Failed');

	// Open by default: it is still how most visits here end. Folding it away is for the visit
	// that came to look at a run.
	let settingsOpen = $state(true);

	const TYPE_LABELS: Record<Source['type'], string> = {
		Rss: 'RSS feed',
		Scraper: 'Web page',
		JsonApi: 'JSON API',
		Webhook: 'Webhook'
	};

	const STATUS_LABELS: Record<Source['status'], string> = {
		Active: 'Running on schedule',
		Paused: 'Paused',
		Failing: 'Stopped after failing'
	};

	// Only worth a column when something has actually been turned away; a source with no filter
	// would otherwise carry a permanent column of dashes.
	const anySkipped = $derived(data.runs.some((run) => run.skippedCount > 0));

	let itemsRun = $state<SourceRun | null>(null);
	let itemsMode = $state<'found' | 'added'>('found');
	let itemsOpen = $state(false);

	let errorRun = $state<SourceRun | null>(null);
	let errorOpen = $state(false);
	let itemsList = $derived(
		itemsMode === 'added' ? (itemsRun?.itemsAdded ?? []) : (itemsRun?.itemsFound ?? [])
	);
	// Runs keep a capped sample rather than every URL, so the list can be shorter than the count.
	let itemsTotal = $derived(
		itemsMode === 'added' ? (itemsRun?.addedCount ?? 0) : (itemsRun?.foundCount ?? 0)
	);
	let itemsTruncated = $derived(itemsTotal > itemsList.length);

	function showItems(run: SourceRun, mode: 'found' | 'added') {
		itemsRun = run;
		itemsMode = mode;
		itemsOpen = true;
	}

	/** A run asked for from this page, being watched until it finishes. */
	let waitingSince = $state<number | null>(null);

	/**
	 * Runs the source now, and watches for the result.
	 *
	 * It used to say "Refresh history in a moment" and leave the refreshing to you, with the
	 * history folded away. Now the history opens and is re-read every few seconds until a run
	 * that started after the click has finished, and the outcome is said.
	 */
	async function runNow() {
		busy = true;
		try {
			const res = await api.post(`/sources/${data.source.id}/run`);
			if (res.status === 202) {
				toast.info('Run started.');
				historyOpen = true;
				void watchForRun(Date.now());
			} else if (res.status === 429) {
				toast.error('Daily run limit reached — try again later.');
			} else {
				toast.error(failureMessage(res.status, 'Could not start a run.'));
			}
		} finally {
			busy = false;
		}
	}

	const POLL_MS = 3000;
	const GIVE_UP_MS = 90_000;

	async function watchForRun(since: number) {
		waitingSince = since;
		// A little slack for clocks: the run's start is stamped by the server.
		const after = since - 5000;
		while (waitingSince === since && Date.now() - since < GIVE_UP_MS) {
			await new Promise((resolve) => setTimeout(resolve, POLL_MS));
			if (waitingSince !== since) return;
			await invalidateAll();

			const run = data.runs.find((r) => new Date(r.startedAt).getTime() >= after && r.finishedAt);
			if (run) {
				waitingSince = null;
				if (run.status === 'Succeeded') {
					toast.success(
						`Run finished: found ${run.foundCount}, added ${run.addedCount}${run.skippedCount ? `, skipped ${run.skippedCount}` : ''}.`
					);
				} else {
					toast.error(`The run ${run.status.toLowerCase()}${run.error ? `: ${run.error}` : '.'}`);
				}
				return;
			}
		}
		if (waitingSince === since) {
			waitingSince = null;
			toast.info('The run is taking a while. Its result will be in the history when it finishes.');
		}
	}

	onDestroy(() => (waitingSince = null));

	/**
	 * Makes another source like this one.
	 *
	 * Everything that shapes what it finds comes along — filters and time zone included, which
	 * the copy used to lose — and a paused or muted original gives a paused or muted copy.
	 * Secrets cannot: the API only ever hands them back redacted, so the copy has to be told
	 * them again, and that is said before rather than discovered on its first failed run.
	 */
	async function duplicate() {
		const hasSecrets = Object.values(data.source.config).some((value) => value === '***');
		const message = hasSecrets
			? `Duplicate "${data.source.name}"? Its passwords and secret headers are not copied — type them into the copy before it runs.`
			: `Duplicate "${data.source.name}"?`;
		if (!(await confirmDialog(message, { confirmLabel: 'Duplicate' }))) return;
		busy = true;
		try {
			const res = await api.post('/sources', {
				name: `${data.source.name} (copy)`,
				type: data.source.type,
				config: data.source.config,
				schedule: data.source.schedule,
				visibility: data.source.visibility,
				playlistIds: data.source.playlistIds,
				timeZone: data.source.timeZone,
				filter: data.source.filter ?? undefined
			});
			if (!res.ok) {
				toast.error('Could not duplicate source.');
				return;
			}

			const created = (await res.json()) as Source;
			// Not settable at creation: a new source always starts active and unmuted.
			const paused = data.source.status !== 'Active';
			if (paused || data.source.muteQuietAlerts) {
				await api.patch(`/sources/${created.id}`, {
					...(paused ? { status: 'Paused' } : {}),
					...(data.source.muteQuietAlerts ? { muteQuietAlerts: true } : {})
				});
			}
			await goto(`/sources/${created.id}`);
		} finally {
			busy = false;
		}
	}

	async function remove() {
		if (!(await confirmDialog('Delete this source? Playlists keep their existing links.', { danger: true, confirmLabel: 'Delete' }))) return;
		busy = true;
		const res = await api.del(`/sources/${data.source.id}`);
		if (res.ok || res.status === 204) {
			await goto('/sources');
		} else {
			busy = false;
			toast.error(failureMessage(res.status, 'Could not delete the source.'));
		}
	}

	let attachedPlaylists = $derived(data.playlists.filter(p => data.source.playlistIds.includes(p.id)));
	let linkOpen = $state(false);
	let linkSearch = $state('');
	let linkResults = $state<Playlist[]>([]);
	let linkCursor = $state<string | null>(null);
	let linkLoading = $state(false);

	function openLinkDialog() {
		linkSearch = '';
		linkResults = [];
		linkCursor = null;
		linkOpen = true;
	}

	async function doLinkSearch(term: string, reset: boolean, cursor?: string) {
		linkLoading = true;
		try {
			const params = new URLSearchParams({ limit: '10' });
			if (term.trim()) params.set('q', term.trim());
			if (!reset && cursor) params.set('cursor', cursor);
			const res = await api.get(`/playlists?${params}`);
			if (!res.ok) return;
			const paged = (await res.json()) as Paged<Playlist>;
			const filtered = paged.items.filter(p => !data.source.playlistIds.includes(p.id));
			linkResults = reset ? filtered : [...linkResults, ...filtered];
			linkCursor = paged.nextCursor;
		} finally {
			linkLoading = false;
		}
	}

	$effect(() => {
		if (!linkOpen) return;
		const term = linkSearch;
		const delay = term ? 300 : 0;
		const t = setTimeout(() => doLinkSearch(term, true), delay);
		return () => clearTimeout(t);
	});

	let mutingQuiet = $state(false);

	/**
	 * Says this source is meant to find nothing, or takes that back.
	 *
	 * A feed that posts twice a year is not broken, and being told about it every week is how
	 * somebody learns to ignore the source that actually is.
	 */
	async function toggleQuietAlerts() {
		mutingQuiet = true;
		const res = await api.patch(`/sources/${data.source.id}`, {
			muteQuietAlerts: !data.source.muteQuietAlerts
		});
		mutingQuiet = false;
		if (res.ok) await invalidateAll();
		else toast.error(failureMessage(res.status, 'Could not change that.'));
	}

	async function linkPlaylist(playlistId: string) {
		const res = await api.patch(`/sources/${data.source.id}`, { playlistIds: [...data.source.playlistIds, playlistId] });
		if (res.ok) {
			linkResults = linkResults.filter(p => p.id !== playlistId);
			await invalidateAll();
		} else {
			toast.error(failureMessage(res.status, 'Could not connect that playlist.'));
		}
	}

	async function unlinkPlaylist(playlistId: string) {
		const before = [...data.source.playlistIds];
		const name = data.playlists.find((p) => p.id === playlistId)?.name ?? 'That playlist';
		const res = await api.patch(`/sources/${data.source.id}`, { playlistIds: before.filter((id) => id !== playlistId) });
		if (!res.ok) {
			toast.error(failureMessage(res.status, 'Could not disconnect that playlist.'));
			return;
		}
		await invalidateAll();
		toast.success(`${name} is no longer fed by this source.`, {
			action: {
				label: 'Undo',
				run: async () => {
					const again = await api.patch(`/sources/${data.source.id}`, { playlistIds: before });
					if (again.ok) await invalidateAll();
					else toast.error(failureMessage(again.status, 'Could not connect it again.'));
				}
			}
		});
	}

	const fmt = formatDate;

	function duration(run: SourceRun) {
		if (!run.finishedAt) return '—';
		const ms = new Date(run.finishedAt).getTime() - new Date(run.startedAt).getTime();
		if (ms < 1000) return `${ms}ms`;
		const seconds = Math.round(ms / 1000);
		if (seconds < 60) return `${seconds}s`;
		const minutes = Math.floor(seconds / 60);
		const remSeconds = seconds % 60;
		if (minutes < 60) return remSeconds ? `${minutes}m ${remSeconds}s` : `${minutes}m`;
		const hours = Math.floor(minutes / 60);
		const remMinutes = minutes % 60;
		return remMinutes ? `${hours}h ${remMinutes}m` : `${hours}h`;
	}
</script>

<svelte:head><title>{data.source.name} - linkbelli</title></svelte:head>

<section class="mx-auto max-w-4xl">
	<BackLink href={backHref} label={backLabel} />

	<header class="mt-3 flex flex-wrap items-start justify-between gap-3">
		<div class="min-w-0 flex-1 basis-64">
			<h1 class="text-2xl font-semibold">{data.source.name}</h1>
			<!-- Where it stands, in one line, before anything else on the page. -->
			<p class="mt-1 text-sm text-muted">
				{TYPE_LABELS[data.source.type]} ·
				<span class={data.source.status === 'Failing' ? 'text-danger' : ''}>{STATUS_LABELS[data.source.status]}</span>
				{#if data.source.lastRunAt}
					· last ran {fmt(data.source.lastRunAt)}{#if data.source.lastRunStatus === 'Failed'},
						<span class="text-danger">and failed</span>{/if}
				{:else}
					· has not run yet
				{/if}
			</p>
		</div>
		<div class="flex shrink-0 items-center gap-2">
			<Button variant="ghost" icon={Copy} iconOnly label="Duplicate source" onclick={duplicate} disabled={busy} />
			<Button icon={Play} onclick={runNow} disabled={busy}>Run now</Button>
		</div>
	</header>


	{#if data.health}
		<div class="mt-6">
			<SourceHealthCard health={data.health} />
		</div>
	{/if}

	{#if data.source.quiet || data.source.muteQuietAlerts}
		<!-- A source that runs cleanly and brings back nothing has no failing status to show, so
		     the only symptom is a playlist that stopped filling — noticed weeks later, if at all.
		     Some sources are meant to be quiet, which is what the button is for: saying so once
		     is help, saying so every week is how somebody learns to ignore the real one. -->
		<div
			class="mt-4 flex flex-wrap items-start justify-between gap-3 rounded-lg border px-4 py-3"
			style="border-color: {data.source.quiet ? 'var(--color-warning)' : 'var(--color-border)'}"
		>
			<div class="min-w-0">
				<p class="text-sm font-medium">
					{data.source.quiet ? 'Running, and finding nothing' : 'Not told about quiet weeks'}
				</p>
				<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
					{data.source.quiet
						? 'Every run this week succeeded and added nothing. Usually that means a selector or a feed address that stopped matching after the site changed — worth opening the settings below and running a preview.'
						: 'This one will not be mentioned in the weekly summary or badged here, however long it goes without finding anything.'}
				</p>
			</div>
			<button
				type="button"
				onclick={toggleQuietAlerts}
				disabled={mutingQuiet}
				class="shrink-0 rounded-md border px-3 py-2 text-sm disabled:opacity-60"
				style="border-color: var(--color-border)"
			>
				{data.source.muteQuietAlerts ? 'Tell me about quiet weeks' : 'Mute quiet-week alerts'}
			</button>
		</div>
	{/if}

	<div class="mt-8 rounded-lg border px-4 py-3" style="border-color: var(--color-border); background: var(--color-surface)">
		<div class="flex items-center justify-between">
			<h2 class="font-medium">Playlists</h2>
			<Button size="sm" icon={Plus} onclick={openLinkDialog}>Connect playlist</Button>
		</div>
		{#if attachedPlaylists.length}
			<ul class="mt-2 flex flex-col gap-2">
				{#each attachedPlaylists as playlist (playlist.id)}
					{@const VisIcon = visIcons[playlist.visibility] ?? Lock}
					<SourceListItem name={playlist.name} href="/playlists/{playlist.id}">
						{#snippet actions()}
							<VisIcon size={13} aria-label={playlist.visibility} style="color: var(--color-muted)" />
							<button
								type="button"
								onclick={() => unlinkPlaylist(playlist.id)}
								title={`Disconnect ${playlist.name}`}
								aria-label={`Disconnect ${playlist.name}`}
								class="inline-flex items-center rounded p-0.5 hover:opacity-70"
								style="color: var(--color-danger)"
							>
								<Unlink size={15} aria-hidden="true" />
							</button>
						{/snippet}
					</SourceListItem>
				{/each}
			</ul>
		{:else}
			<p class="mt-2 text-sm" style="color: var(--color-muted)">No playlists connected.</p>
		{/if}
	</div>

	<div class="mt-8">
		<div class="flex items-center justify-between">
			<button
				type="button"
				onclick={() => (historyOpen = !historyOpen)}
				class="inline-flex items-center gap-1 font-medium hover:opacity-70"
				aria-expanded={historyOpen}
			>
				<ChevronRight
					size={16}
					aria-hidden="true"
					class="transition-transform duration-150"
					style={historyOpen ? 'transform: rotate(90deg)' : ''}
				/>
				Run history
			</button>
			{#if waitingSince !== null}
				<span class="inline-flex items-center gap-1.5 text-sm text-muted" role="status">
					<LoaderCircle size={14} aria-hidden="true" class="animate-spin" /> Running now…
				</span>
			{:else if historyOpen}
				<Button variant="ghost" size="sm" icon={RotateCcw} iconOnly label="Refresh run history" onclick={() => invalidateAll()} />
			{/if}
		</div>
		{#if historyOpen}
		{#if data.runs.length === 0}
			<p class="mt-2 text-sm" style="color: var(--color-muted)">No runs yet.</p>
		{:else}
			<div class="mt-2 overflow-x-auto">
			<table class="w-full border-collapse text-sm">
				<thead>
					<tr class="text-left" style="color: var(--color-muted)">
						<th class="py-1 font-medium">Status</th>
						<th class="py-1 font-medium">Found</th>
						<th class="py-1 font-medium">Added</th>
						{#if anySkipped}
							<th class="py-1 font-medium">Skipped</th>
						{/if}
						<th class="py-1 font-medium">Started</th>
						<th class="py-1 font-medium">Duration</th>
					</tr>
				</thead>
				<tbody>
					{#each pagedRuns as run (run.id)}
						<tr class="border-t" style="border-color: var(--color-border)">
							<td class="py-1">
								{#if run.status !== 'Succeeded' && run.error}
									<button
										type="button"
										onclick={() => { errorRun = run; errorOpen = true; }}
										class="rounded px-1.5 py-0.5 underline-offset-2 hover:underline"
										style="color: var(--color-danger)"
									>
										{run.status}
									</button>
								{:else}
									{run.status}
								{/if}
							</td>
							<td class="py-1">
								{#if run.foundCount > 0}
									<button
										type="button"
										onclick={() => showItems(run, 'found')}
										class="rounded px-1.5 py-0.5 underline-offset-2 hover:underline"
										style="color: var(--color-accent)"
										title="Show items found"
										aria-label="Show items found"
									>
										{run.foundCount}
									</button>
								{:else}
									<button type="button" disabled class="rounded px-1.5 py-0.5 opacity-30 cursor-default">0</button>
								{/if}
							</td>
							<td class="py-1">
								{#if run.addedCount > 0}
									<button
										type="button"
										onclick={() => showItems(run, 'added')}
										class="rounded px-1.5 py-0.5 underline-offset-2 hover:underline"
										style="color: var(--color-accent)"
										title="Show items added"
										aria-label="Show items added"
									>
										{run.addedCount}
									</button>
								{:else}
									<button type="button" disabled class="rounded px-1.5 py-0.5 opacity-30 cursor-default">0</button>
								{/if}
							</td>
							{#if anySkipped}
								<td class="py-1 tabular-nums" style="color: var(--color-muted)">{run.skippedCount || '—'}</td>
							{/if}
							<td class="py-1" style="color: var(--color-muted)">
								<time datetime={run.startedAt} title={formatExact(run.startedAt)}>{fmt(run.startedAt)}</time>
							</td>
							<td class="py-1" style="color: var(--color-muted)">{duration(run)}</td>
						</tr>
					{/each}
				</tbody>
			</table>
			</div>
			{#if data.runs.length > shownRuns}
				<LoadMore onclick={() => (shownRuns += pageSize)} remaining={data.runs.length - shownRuns} />
			{/if}
		{/if}
		{/if}
	</div>

	<!-- What the source is set to do. Last, because a source you open is usually one you want to
	     check on, and the form is long enough to push everything else off the screen. -->
	<div class="mt-8 border-t pt-6">
		<button
			type="button"
			onclick={() => (settingsOpen = !settingsOpen)}
			class="inline-flex items-center gap-1 font-medium hover:opacity-70"
			aria-expanded={settingsOpen}
			aria-controls="source-settings"
		>
			<ChevronRight
				size={16}
				aria-hidden="true"
				class="transition-transform duration-150"
				style={settingsOpen ? 'transform: rotate(90deg)' : ''}
			/>
			Settings
		</button>
		<div id="source-settings" class="mt-4" hidden={!settingsOpen}>
			{#key data.source.id}
				<SourceForm mode="edit" source={data.source} ondelete={remove} />
			{/key}
		</div>
	</div>
</section>

<Modal bind:open={linkOpen} title="Connect a playlist" size="sm">
	<Input
		bind:value={linkSearch}
		placeholder="Search…"
		aria-label="Search playlists"
		size="sm"
		class="w-full"
	/>
	<div class="mt-2 flex-1 overflow-y-auto">
		{#if linkLoading && linkResults.length === 0}
			<p class="py-2 text-sm" style="color: var(--color-muted)">Loading…</p>
		{:else if linkResults.length === 0}
			<p class="py-2 text-sm" style="color: var(--color-muted)">{linkSearch.trim() ? 'No matches.' : 'No playlists to connect.'}</p>
		{:else}
			<ul class="flex flex-col gap-1">
				{#each linkResults as pl (pl.id)}
					{@const VisIcon = visIcons[pl.visibility] ?? Lock}
					<li>
						<button
							type="button"
							onclick={() => linkPlaylist(pl.id)}
							class="flex w-full items-center justify-between rounded-md px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
						>
							<span class="truncate">{pl.name}</span>
							<VisIcon size={13} aria-label={pl.visibility} class="ml-2 shrink-0" style="color: var(--color-muted)" />
						</button>
					</li>
				{/each}
			</ul>
			{#if linkCursor}
				<LoadMore onclick={() => doLinkSearch(linkSearch, false, linkCursor ?? undefined)} loading={linkLoading} />
			{/if}
		{/if}
	</div>
</Modal>

<Modal bind:open={errorOpen} title="Run failed" size="lg">
	<pre
		class="whitespace-pre-wrap break-words rounded p-3 text-xs"
		style="background: var(--color-bg); color: var(--color-danger)">{errorRun?.error ?? ''}</pre>
</Modal>

<Modal
	bind:open={itemsOpen}
	title={`${itemsMode === 'added' ? 'Added' : 'Found'} ${plural(itemsTotal, 'link')}`}
	description={itemsTruncated
		? `Showing the first ${itemsList.length}. Run history keeps a sample, not every address.`
		: undefined}
	size="lg"
>
	<div class="overflow-x-auto text-sm">
		<ul class="pb-2">
			{#each itemsList as url (url)}
				<li class="border-t py-1.5 first:border-t-0" style="border-color: var(--color-border)">
					<span class="whitespace-nowrap">{url}</span>
				</li>
			{/each}
		</ul>
	</div>
</Modal>
