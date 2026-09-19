<script lang="ts">
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
	import { api } from '$lib/api/client';
	import type { HighlightWithSource } from '$lib/highlights';
	import type { Paged } from '$lib/types';
	import { Highlighter } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let items = $state(data.highlights.items);
	let nextCursor = $state(data.highlights.nextCursor);
	let loadingMore = $state(false);

	async function loadMore() {
		if (!nextCursor || loadingMore) return;

		loadingMore = true;
		try {
			const res = await api.get(`/highlights?limit=50&cursor=${encodeURIComponent(nextCursor)}`);
			if (res.ok) {
				const page = (await res.json()) as Paged<HighlightWithSource>;
				items = [...items, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}

	function when(iso: string): string {
		return new Date(iso).toLocaleDateString(undefined, {
			month: 'short',
			day: 'numeric',
			year: 'numeric'
		});
	}
</script>

<svelte:head><title>Highlights - linkbelli</title></svelte:head>

<!-- Everything marked, across the whole library. The highest-signal text somebody has: it is the
     part they stopped at and chose, and until now it had nowhere to be read back together. -->
<section class="mx-auto max-w-3xl">
	<header>
		<h1 class="text-2xl font-semibold">Highlights</h1>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Everything you have marked in the articles you saved, newest first.
		</p>
	</header>

	{#if items.length === 0}
		<div
			class="mt-8 flex flex-col items-center gap-2 rounded-lg border px-6 py-10 text-center text-sm"
			style="border-color: var(--color-border); color: var(--color-muted)"
		>
			<Highlighter size={22} aria-hidden="true" />
			<p>Nothing marked yet.</p>
			<p class="max-w-sm">
				Open an article in the reader and select a passage — a button turns up to highlight it,
				or press <kbd>h</kbd>.
			</p>
		</div>
	{:else}
		<ul class="mt-6 flex flex-col gap-6">
			{#each items as h (h.id)}
				<li>
					<blockquote
						class="border-l-2 pl-3 leading-relaxed"
						style="border-color: var(--color-highlight-strong)"
					>
						{h.text}
					</blockquote>
					{#if h.note}
						<p class="mt-1.5 whitespace-pre-line pl-3 text-sm">{h.note}</p>
					{/if}
					<p class="mt-1.5 pl-3 text-xs" style="color: var(--color-muted)">
						<a href={`/read/${h.linkId}`} class="font-medium hover:underline">
							{h.title ?? h.url}
						</a>
						· {h.siteName ?? h.host} · {when(h.createdAt)}
					</p>
				</li>
			{/each}
		</ul>

		{#if nextCursor}
			<LoadMore onclick={loadMore} loading={loadingMore} />
		{/if}
	{/if}
</section>
