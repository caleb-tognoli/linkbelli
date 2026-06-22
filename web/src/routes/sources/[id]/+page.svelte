<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { page as routePage } from '$app/state';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Dialog } from 'bits-ui';
	import { Play, RotateCcw, X, ChevronRight, Copy, Plus, Unlink, Lock, EyeOff, Globe, ChevronDown } from '@lucide/svelte';

	const visIcons = { Private: Lock, Unlisted: EyeOff, Public: Globe } as const;
	import SourceForm from '$lib/components/SourceForm.svelte';
	import SourceListItem from '$lib/components/SourceListItem.svelte';
	import type { PageData } from './$types';
	import type { Paged, Playlist, Source, SourceRun } from '$lib/types';

	let { data }: { data: PageData } = $props();
	let busy = $state(false);
	let toast = $state<string | null>(null);

	const backHref = $derived(routePage.url.searchParams.get('from') ?? '/#sources');
	const backLabel = $derived(routePage.url.searchParams.get('fromLabel') ?? 'Sources');

	const pageSize = 10;
	let page = $state(0);
	let totalPages = $derived(Math.max(1, Math.ceil(data.runs.length / pageSize)));
	let pagedRuns = $derived(data.runs.slice(page * pageSize, page * pageSize + pageSize));

	let historyOpen = $state(false);

	let itemsRun = $state<SourceRun | null>(null);
	let itemsMode = $state<'found' | 'added'>('found');
	let itemsOpen = $state(false);

	let errorRun = $state<SourceRun | null>(null);
	let errorOpen = $state(false);
	let itemsList = $derived(
		itemsMode === 'added' ? (itemsRun?.itemsAdded ?? []) : (itemsRun?.itemsFound ?? [])
	);

	function showItems(run: SourceRun, mode: 'found' | 'added') {
		itemsRun = run;
		itemsMode = mode;
		itemsOpen = true;
	}

	async function runNow() {
		busy = true;
		try {
			const res = await api.post(`/sources/${data.source.id}/run`);
			if (res.status === 202) toast = 'Run queued. Refresh history in a moment.';
			else if (res.status === 429) toast = 'Daily run limit reached — try again later.';
			else toast = 'Could not queue a run.';
		} finally {
			busy = false;
		}
	}

	async function duplicate() {
		if (!(await confirmDialog(`Duplicate "${data.source.name}"?`, { confirmLabel: 'Duplicate' }))) return;
		busy = true;
		try {
			const res = await api.post('/sources', {
				name: `${data.source.name} (copy)`,
				type: data.source.type,
				config: data.source.config,
				schedule: data.source.schedule,
				visibility: data.source.visibility,
				playlistIds: data.source.playlistIds,
			});
			if (res.ok) {
				const created = (await res.json()) as Source;
				await goto(`/sources/${created.id}`);
			} else {
				toast = 'Could not duplicate source.';
			}
		} finally {
			busy = false;
		}
	}

	async function remove() {
		if (!(await confirmDialog('Delete this source? Playlists keep their existing links.', { danger: true, confirmLabel: 'Delete' }))) return;
		busy = true;
		const res = await api.del(`/sources/${data.source.id}`);
		if (res.ok || res.status === 204) await goto('/#sources');
		else busy = false;
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

	async function linkPlaylist(playlistId: string) {
		const res = await api.patch(`/sources/${data.source.id}`, { playlistIds: [...data.source.playlistIds, playlistId] });
		if (res.ok) {
			linkResults = linkResults.filter(p => p.id !== playlistId);
			await invalidateAll();
		}
	}

	async function unlinkPlaylist(playlistId: string) {
		const res = await api.patch(`/sources/${data.source.id}`, { playlistIds: data.source.playlistIds.filter(id => id !== playlistId) });
		if (res.ok) await invalidateAll();
	}

	function fmt(iso: string | null) {
		return iso ? new Date(iso).toLocaleString() : '—';
	}

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

<svelte:head><title>{data.source.name} — Linkbelli</title></svelte:head>

<section class="mx-auto max-w-4xl">
	<a href={backHref} class="text-sm" style="color: var(--color-muted)">← {backLabel}</a>

	<header class="mt-3 flex items-center justify-between gap-3">
		<h1 class="text-2xl font-semibold">{data.source.name}</h1>
		<div class="flex items-center gap-1">
			<button type="button" onclick={duplicate} disabled={busy} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" title="Duplicate source" aria-label="Duplicate source">
				<Copy size={17} aria-hidden="true" />
			</button>
			<button type="button" onclick={runNow} disabled={busy} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" title="Run now" aria-label="Run now">
				<Play size={17} aria-hidden="true" />
			</button>
		</div>
	</header>

	{#if toast}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
	{/if}

	{#key data.source.id}
		<div class="mt-5">
			<SourceForm mode="edit" source={data.source} ondelete={remove} />
		</div>
	{/key}

	<div class="mt-8 rounded-lg border px-4 py-3" style="border-color: var(--color-border); background: var(--color-surface)">
		<div class="flex items-center justify-between">
			<h2 class="font-medium">Playlists</h2>
			<button
				type="button"
				onclick={openLinkDialog}
				class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
				title="Link playlist"
				aria-label="Link playlist"
			>
				<Plus size={16} aria-hidden="true" />
			</button>
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
								title="Unlink"
								aria-label="Unlink"
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
			<p class="mt-2 text-sm" style="color: var(--color-muted)">No playlists linked.</p>
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
			{#if historyOpen}
				<button type="button" onclick={() => { toast = null; invalidateAll(); }} class="inline-flex items-center rounded p-1" style="color: var(--color-muted)" title="Refresh" aria-label="Refresh run history">
					<RotateCcw size={15} aria-hidden="true" />
				</button>
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
								{#if run.itemsFound.length > 0}
									<button
										type="button"
										onclick={() => showItems(run, 'found')}
										class="rounded px-1.5 py-0.5 underline-offset-2 hover:underline"
										style="color: var(--color-accent)"
										title="Show items found"
										aria-label="Show items found"
									>
										{run.itemsFound.length}
									</button>
								{:else}
									<button type="button" disabled class="rounded px-1.5 py-0.5 opacity-30 cursor-default">0</button>
								{/if}
							</td>
							<td class="py-1">
								{#if run.itemsAdded.length > 0}
									<button
										type="button"
										onclick={() => showItems(run, 'added')}
										class="rounded px-1.5 py-0.5 underline-offset-2 hover:underline"
										style="color: var(--color-accent)"
										title="Show items added"
										aria-label="Show items added"
									>
										{run.itemsAdded.length}
									</button>
								{:else}
									<button type="button" disabled class="rounded px-1.5 py-0.5 opacity-30 cursor-default">0</button>
								{/if}
							</td>
							<td class="py-1" style="color: var(--color-muted)">{fmt(run.startedAt)}</td>
							<td class="py-1" style="color: var(--color-muted)">{duration(run)}</td>
						</tr>
					{/each}
				</tbody>
			</table>
			</div>
			{#if totalPages > 1}
				<div class="mt-2 flex items-center justify-center gap-3 text-sm" style="color: var(--color-muted)">
					<button
						type="button"
						onclick={() => (page = Math.max(0, page - 1))}
						disabled={page === 0}
						class="rounded px-2 py-1 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-40"
					>
						Prev
					</button>
					<span>Page {page + 1} of {totalPages}</span>
					<button
						type="button"
						onclick={() => (page = Math.min(totalPages - 1, page + 1))}
						disabled={page >= totalPages - 1}
						class="rounded px-2 py-1 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-40"
					>
						Next
					</button>
				</div>
			{/if}
		{/if}
		{/if}
	</div>
</section>

<Dialog.Root bind:open={linkOpen}>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 flex max-h-[70vh] w-[90vw] max-w-sm -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex shrink-0 items-center justify-between">
				<Dialog.Title class="font-semibold">Link playlist</Dialog.Title>
				<Dialog.Close class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10" title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>
			<input
				bind:value={linkSearch}
				placeholder="Search…"
				aria-label="Search playlists"
				class="mt-3 shrink-0 w-full rounded-md border px-3 py-1.5 text-sm"
				style="border-color: var(--color-border); background: var(--color-bg)"
			/>
			<div class="mt-2 flex-1 overflow-y-auto">
				{#if linkLoading && linkResults.length === 0}
					<p class="py-2 text-sm" style="color: var(--color-muted)">Loading…</p>
				{:else if linkResults.length === 0}
					<p class="py-2 text-sm" style="color: var(--color-muted)">{linkSearch.trim() ? 'No matches.' : 'No playlists to link.'}</p>
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
						<button
							type="button"
							onclick={() => doLinkSearch(linkSearch, false, linkCursor ?? undefined)}
							disabled={linkLoading}
							class="mt-1 flex w-full items-center justify-center rounded-md py-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-40"
							style="color: var(--color-muted)"
							title="Show more"
							aria-label="Show more"
						>
							<ChevronDown size={16} aria-hidden="true" />
						</button>
					{/if}
				{/if}
			</div>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>

<Dialog.Root bind:open={errorOpen}>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-start justify-between gap-4">
				<Dialog.Title class="text-lg font-semibold">Run failed</Dialog.Title>
				<Dialog.Close class="shrink-0 inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10" title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>
			<div class="mt-4 max-h-96 overflow-y-auto text-sm">
				<pre class="whitespace-pre-wrap break-words rounded p-3 text-xs" style="background: var(--color-surface); color: var(--color-danger)">{errorRun?.error ?? ''}</pre>
			</div>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>

<Dialog.Root bind:open={itemsOpen}>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-start justify-between gap-4">
				<Dialog.Title class="text-lg font-semibold">
					{itemsMode === 'added' ? 'Added' : 'Found'} {itemsList.length} items
				</Dialog.Title>
				<Dialog.Close class="shrink-0 inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10" title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>

			<div class="mt-4 max-h-96 overflow-x-auto overflow-y-auto text-sm">
				<ul class="pb-2">
					{#each itemsList as url (url)}
						<li class="border-t py-1.5 first:border-t-0" style="border-color: var(--color-border)">
							<span class="whitespace-nowrap">{url}</span>
						</li>
					{/each}
				</ul>
			</div>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
