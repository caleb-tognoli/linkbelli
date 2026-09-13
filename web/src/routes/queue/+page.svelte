<svelte:head><title>Up next - linkbelli</title></svelte:head>

<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import NsfwBadge from '$lib/components/NsfwBadge.svelte';
	import { BookOpen, Check, Star } from '@lucide/svelte';
	import type { SearchHit } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state<string | null>(null);

	async function markWatched(itemId: string) {
		busy = itemId;
		const res = await api.patch(`/items/${itemId}`, { status: 'Watched' });
		busy = null;
		if (res.ok) await invalidateAll();
	}

	/**
	 * The two halves of a queue.
	 *
	 * Something started is the cheapest thing to finish, so it leads — and until progress was
	 * recorded, a twenty-two-minute piece read half of on the train looked exactly like one
	 * never opened, and kept being offered from the top.
	 */
	const started = $derived(data.queue.items.filter((h) => (h.readProgress ?? 0) > 0.02));
	const fresh = $derived(data.queue.items.filter((h) => (h.readProgress ?? 0) <= 0.02));

	/** How long something has been waiting — the reason it is near the top. */
	function waiting(iso: string): string {
		const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
		if (days < 1) return 'today';
		if (days === 1) return 'since yesterday';
		if (days < 30) return `for ${days} days`;
		const months = Math.floor(days / 30);
		return months < 12 ? `for ${months} months` : `for over a year`;
	}
</script>

<section class="mx-auto max-w-3xl">
	<header>
		<h1 class="text-2xl font-semibold">Up next</h1>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Everything you have not got to yet, across every playlist — what you started first, then
			what you rated highly, then whatever you have been carrying longest.
		</p>
	</header>

	{#if data.queue.items.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing waiting.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Everything you have saved is marked watched.
			</p>
		</div>
	{:else}
		<p class="mt-5 text-sm" style="color: var(--color-muted)">
			{data.queue.total} waiting
		</p>

		{#if started.length > 0}
			<h2 class="mt-5 text-sm font-medium">Carry on</h2>
			<p class="text-xs" style="color: var(--color-muted)">
				Part way through. The quickest things to finish.
			</p>
			<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
				{#each started as hit (hit.itemId)}
					{@render row(hit)}
				{/each}
			</ul>

			<h2 class="mt-6 text-sm font-medium">Not started</h2>
		{/if}

		<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each fresh as hit (hit.itemId)}
				{@render row(hit)}
			{/each}
		</ul>
	{/if}
</section>

{#snippet row(hit: SearchHit)}
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
						>{hit.link.title ?? hit.link.url}</a>
						{#if hit.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}

						{#if hit.note}
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">{hit.note}</p>
						{/if}

						<p class="mt-1 flex flex-wrap items-center gap-x-2 text-xs" style="color: var(--color-muted)">
							<a href={`/playlists/${hit.playlistId}`} class="hover:underline">{hit.playlistName}</a>
							<span aria-hidden="true">·</span>
							<span>waiting {waiting(hit.addedAt)}</span>
							{#if (hit.readProgress ?? 0) > 0.02}
								<span aria-hidden="true">·</span>
								<a
									href={`/read/${hit.link.id}?from=${hit.playlistId}`}
									class="inline-flex items-center gap-1 hover:underline"
								>
									<BookOpen size={12} aria-hidden="true" />
									{Math.round((hit.readProgress ?? 0) * 100)}% read
								</a>
							{:else if hit.link.wordCount}
								<span aria-hidden="true">·</span>
								<a
									href={`/read/${hit.link.id}?from=${hit.playlistId}`}
									class="inline-flex items-center gap-1 hover:underline"
								>
									<BookOpen size={12} aria-hidden="true" /> Read it here
								</a>
							{/if}
							{#if hit.score !== null}
								<span aria-hidden="true">·</span>
								<span class="inline-flex items-center gap-1 tabular-nums">
									<Star size={12} aria-hidden="true" /> {hit.score}
								</span>
							{/if}
						</p>
					</div>

					<button
						type="button"
						onclick={() => markWatched(hit.itemId)}
						disabled={busy !== null}
						class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
						title="Mark watched"
					>
			<Check size={14} aria-hidden="true" /> Done
		</button>
	</li>
{/snippet}
