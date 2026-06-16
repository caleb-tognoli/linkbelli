<script lang="ts">
	import { api } from '$lib/api/client';
	import { Search, ChevronDown } from '@lucide/svelte';
	import PublicPlaylistCard from '$lib/components/PublicPlaylistCard.svelte';
	import TagFilter from '$lib/components/TagFilter.svelte';
	import type { Paged, PublicPlaylistSummary } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let items = $state(data.results.items);
	let nextCursor = $state(data.results.nextCursor);
	let loadingMore = $state(false);

	// Reset the (client-appended) list when a new filter/search loads. Plain marker avoids a loop.
	let seen = data.results;
	$effect(() => {
		if (data.results !== seen) {
			seen = data.results;
			items = data.results.items;
			nextCursor = data.results.nextCursor;
		}
	});

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const qs = new URLSearchParams();
			if (data.q) qs.set('q', data.q);
			for (const t of data.activeTags) qs.append('tag', t);
			qs.set('cursor', nextCursor);
			const res = await api.get(`/public/playlists?${qs}`);
			if (res.ok) {
				const page = (await res.json()) as Paged<PublicPlaylistSummary>;
				items = [...items, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}
</script>

<section class="mx-auto max-w-5xl">
	<h1 class="text-2xl font-semibold">Discover</h1>
	<p class="mt-1 text-sm" style="color: var(--color-muted)">Browse public playlists.</p>

	<form method="get" class="mt-4 flex gap-2">
		<input
			name="q"
			value={data.q}
			placeholder="Search public playlists…"
			aria-label="Search public playlists"
			class="flex-1 rounded-md border px-3 py-2 text-sm"
			style="border-color: var(--color-border); background: var(--color-bg)"
		/>
		<button type="submit" class="rounded-md p-2" style="background: var(--color-accent); color: var(--color-accent-contrast)" title="Search" aria-label="Search">
			<Search size={18} aria-hidden="true" />
		</button>
	</form>

	<div class="mt-4">
		<TagFilter active={data.activeTags} basePath="/discover" suggestPath="/public/tags" extraParams={{ q: data.q }} />
	</div>

	{#if items.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing found.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				{data.q || data.activeTags.length ? 'Try a different search or tag.' : 'No public playlists yet.'}
			</p>
		</div>
	{:else}
		<div class="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each items as playlist (playlist.ownerUsername + '/' + playlist.slug)}
				<PublicPlaylistCard {playlist} />
			{/each}
		</div>
		{#if nextCursor}
			<div class="mt-4 text-center">
				<button type="button" onclick={loadMore} disabled={loadingMore} class="rounded-md border p-1.5 disabled:opacity-60" style="border-color: var(--color-border)" title="Load more" aria-label="Load more">
					<ChevronDown size={18} aria-hidden="true" />
				</button>
			</div>
		{/if}
	{/if}
</section>
