<svelte:head><title>Search - linkbelli</title></svelte:head>

<script lang="ts">
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { AlertCircle, Search, Eye, Star, X } from '@lucide/svelte';
	import NsfwBadge from '$lib/components/NsfwBadge.svelte';
	import type { Paged, SearchHit } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let term = $state(data.q);
	let hits = $state<SearchHit[]>(data.results.items);
	let nextCursor = $state<string | null>(data.results.nextCursor);
	let total = $state<number | null>(data.results.total ?? null);
	let loadingMore = $state(false);

	// The server load is the source of truth after a navigation; re-sync when it changes.
	$effect(() => {
		hits = data.results.items;
		nextCursor = data.results.nextCursor;
		total = data.results.total ?? null;
		term = data.q;
	});

	const statuses = [
		{ value: '', label: 'All' },
		{ value: 'unwatched', label: 'Unwatched' },
		{ value: 'watched', label: 'Watched' }
	];

	/** The search state lives in the URL, so a result list is a link someone can keep. */
	function navigate(changes: Record<string, string>) {
		const params = new URLSearchParams();
		const next = {
			q: term,
			host: data.host,
			status: data.status,
			finished: data.finished,
			broken: data.broken,
			...changes
		};
		for (const [key, value] of Object.entries(next)) {
			if (value) params.set(key, value);
		}
		goto(`/search?${params}`, { keepFocus: true, noScroll: true });
	}

	let debounce: ReturnType<typeof setTimeout>;
	function onInput() {
		clearTimeout(debounce);
		debounce = setTimeout(() => navigate({ q: term }), 300);
	}

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const params = new URLSearchParams({ limit: '25', cursor: nextCursor });
			if (data.q) params.set('q', data.q);
			if (data.host) params.set('host', data.host);
			if (data.status) params.set('status', data.status);
			if (data.broken) params.set('broken', 'true');
			if (data.finished) {
				params.set(
					'finishedSince',
					new Date(Date.now() - Number(data.finished) * 86_400_000).toISOString()
				);
			}

			const res = await api.get(`/search?${params}`);
			if (res.ok) {
				const page = (await res.json()) as Paged<SearchHit>;
				hits = [...hits, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}
</script>

<section class="mx-auto max-w-4xl">
	<h1 class="text-2xl font-semibold">Search</h1>
	<p class="mt-1 text-sm" style="color: var(--color-muted)">
		Across every playlist you own — titles, descriptions, notes, and addresses.
	</p>

	<div class="relative mt-5">
		<Search
			size={16}
			aria-hidden="true"
			class="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2"
			style="color: var(--color-muted)"
		/>
		<input
			bind:value={term}
			oninput={onInput}
			placeholder="Search your links…"
			aria-label="Search your links"
			class="w-full rounded-md border py-2.5 pr-3 pl-9"
			style="border-color: var(--color-border); background: var(--color-bg)"
		/>
	</div>

	<div class="mt-3 flex flex-wrap items-center gap-2 text-sm">
		<div class="inline-flex divide-x overflow-hidden rounded-md border" style="border-color: var(--color-border)">
			{#each statuses as option (option.value)}
				<button
					type="button"
					onclick={() => navigate({ status: option.value })}
					class="px-3 py-1.5"
					class:font-medium={data.status === option.value}
					style="background: {data.status === option.value ? 'var(--color-surface)' : 'var(--color-bg)'}"
				>{option.label}</button>
			{/each}
		</div>

		<button
			type="button"
			onclick={() => navigate({ finished: data.finished ? '' : '7', status: '' })}
			class="rounded-md border px-3 py-1.5"
			class:font-medium={!!data.finished}
			style="border-color: {data.finished ? 'var(--color-accent)' : 'var(--color-border)'};
			       color: {data.finished ? 'var(--color-accent)' : 'inherit'}"
			title="Items you marked watched in the last week"
		>Finished this week</button>

		<button
			type="button"
			onclick={() => navigate({ broken: data.broken ? '' : '1' })}
			class="rounded-md border px-3 py-1.5"
			class:font-medium={!!data.broken}
			style="border-color: {data.broken ? 'var(--color-danger)' : 'var(--color-border)'};
			       color: {data.broken ? 'var(--color-danger)' : 'inherit'}"
			title="Links whose page is gone or can no longer be read"
		>Broken</button>

		{#if data.host}
			<button
				type="button"
				onclick={() => navigate({ host: '' })}
				class="inline-flex items-center gap-1 rounded-md border px-2.5 py-1.5"
				style="border-color: var(--color-accent); color: var(--color-accent)"
			>
				{data.host}
				<X size={13} aria-hidden="true" />
			</button>
		{/if}

		{#if total !== null}
			<span class="ml-auto tabular-nums" style="color: var(--color-muted)">
				{total} {total === 1 ? 'result' : 'results'}
			</span>
		{/if}
	</div>

	{#if data.hosts.length > 1 && !data.host}
		<div class="mt-3 flex flex-wrap gap-1.5">
			{#each data.hosts.slice(0, 8) as facet (facet.hostname)}
				<button
					type="button"
					onclick={() => navigate({ host: facet.hostname })}
					class="rounded-md border px-2 py-1 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border); color: var(--color-muted)"
				>
					{facet.hostname}
					<span class="tabular-nums opacity-60">{facet.itemCount}</span>
				</button>
			{/each}
		</div>
	{/if}

	{#if hits.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">
				{data.q || data.host || data.status || data.finished || data.broken ? 'Nothing matched.' : 'Search across everything you have saved.'}
			</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				{data.q || data.host || data.status || data.finished || data.broken
					? 'Try fewer words, or a different filter.'
					: 'Type above, or pick a site to browse what you saved from it.'}
			</p>
		</div>
	{:else}
		<ul class="mt-5 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each hits as hit (hit.itemId)}
				<li class="flex items-start gap-3 p-3" style="border-color: var(--color-border)">
					{#if hit.link.favicon}
						<img src={hit.link.favicon} alt="" class="mt-0.5 size-4 shrink-0 object-contain" loading="lazy" />
					{:else}
						<span class="mt-0.5 size-4 shrink-0 rounded-sm" style="background: var(--color-border)"></span>
					{/if}

					<div class="min-w-0 flex-1">
						<a
							href={hit.link.url}
							target="_blank"
							rel="noopener noreferrer"
							class="break-words font-medium hover:underline"
						>
							{hit.link.title ?? hit.link.url}
						</a>
						{#if hit.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}

						{#if hit.note}
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">{hit.note}</p>
						{/if}

						<p class="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs" style="color: var(--color-muted)">
							<a href={`/playlists/${hit.playlistId}`} class="hover:underline">{hit.playlistName}</a>
							<span aria-hidden="true">·</span>
							<button type="button" onclick={() => navigate({ host: hit.link.host })} class="hover:underline">
								{hit.link.host}
							</button>
							{#if hit.link.enrichmentStatus === 'Broken' || hit.link.enrichmentStatus === 'Failed'}
								<span aria-hidden="true">·</span>
								<span
									class="inline-flex items-center gap-1"
									style="color: {hit.link.enrichmentStatus === 'Broken' ? 'var(--color-danger)' : 'inherit'}"
									title={hit.link.enrichmentError ?? undefined}
								>
									<AlertCircle size={12} aria-hidden="true" />
									{hit.link.enrichmentStatus === 'Broken' ? 'page gone' : 'unreadable'}
								</span>
							{/if}
							{#if hit.status === 'Watched'}
								<span aria-hidden="true">·</span>
								<span class="inline-flex items-center gap-1"><Eye size={12} aria-hidden="true" /> watched</span>
							{/if}
							{#if hit.score !== null}
								<span aria-hidden="true">·</span>
								<span class="inline-flex items-center gap-1 tabular-nums">
									<Star size={12} aria-hidden="true" /> {hit.score}
								</span>
							{/if}
						</p>
					</div>
				</li>
			{/each}
		</ul>

		{#if nextCursor}
			<button
				type="button"
				onclick={loadMore}
				disabled={loadingMore}
				class="mt-3 w-full rounded-md border py-2 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				{loadingMore ? 'Loading…' : 'Load more'}
			</button>
		{/if}
	{/if}
</section>
