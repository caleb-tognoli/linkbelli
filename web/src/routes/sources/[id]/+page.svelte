<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { page as routePage } from '$app/state';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Dialog } from 'bits-ui';
	import { Play, RotateCcw, X, ChevronRight } from '@lucide/svelte';
	import SourceForm from '$lib/components/SourceForm.svelte';
	import type { PageData } from './$types';
	import type { SourceRun } from '$lib/types';

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

	async function remove() {
		if (!(await confirmDialog('Delete this source? Playlists keep their existing links.', { danger: true, confirmLabel: 'Delete' }))) return;
		busy = true;
		const res = await api.del(`/sources/${data.source.id}`);
		if (res.ok || res.status === 204) await goto('/#sources');
		else busy = false;
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
		<button type="button" onclick={runNow} disabled={busy} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" title="Run now" aria-label="Run now">
			<Play size={17} aria-hidden="true" />
		</button>
	</header>

	{#if toast}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
	{/if}

	{#key data.source.id}
		<div class="mt-5">
			<SourceForm mode="edit" source={data.source} ondelete={remove} />
		</div>
	{/key}

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
