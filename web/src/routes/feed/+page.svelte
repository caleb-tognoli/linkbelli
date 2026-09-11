<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import KindBadge from '$lib/components/KindBadge.svelte';
	import { readingLabel } from '$lib/reading';
	import { Check, ChevronDown, Rss } from '@lucide/svelte';
	import type { Feed, FeedItem } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let items = $state(data.feed.items);
	let nextCursor = $state(data.feed.nextCursor);
	let loadingMore = $state(false);
	let marking = $state(false);

	// Captured once, on arrival: marking the feed seen shouldn't make the "new" line vanish out
	// from under someone who is still reading it.
	const seenAt = data.feed.lastSeenAt;
	let newCount = $state(data.feed.newCount);

	const followCount = $derived(data.following.playlists.length + data.following.users.length);

	function isNew(item: FeedItem): boolean {
		return !seenAt || item.addedAt > seenAt;
	}

	async function loadMore() {
		if (!nextCursor || loadingMore) return;

		loadingMore = true;
		try {
			const res = await api.get(`/feed?limit=30&cursor=${encodeURIComponent(nextCursor)}`);
			if (res.ok) {
				const page = (await res.json()) as Feed;
				items = [...items, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}

	async function markSeen() {
		marking = true;
		try {
			if ((await api.post('/feed/seen')).ok) {
				newCount = 0;
				await invalidateAll();
			}
		} finally {
			marking = false;
		}
	}

	function when(iso: string): string {
		return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
	}
</script>

<svelte:head><title>Feed - linkbelli</title></svelte:head>

<section class="mx-auto max-w-3xl">
	<header class="flex items-start justify-between gap-4">
		<div>
			<h1 class="text-2xl font-semibold">Feed</h1>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				{#if followCount === 0}
					Follow a playlist or a person and new links will turn up here.
				{:else}
					New links from {followCount} {followCount === 1 ? 'thing' : 'things'} you follow.
				{/if}
			</p>
		</div>
		{#if newCount > 0}
			<button
				type="button"
				onclick={markSeen}
				disabled={marking}
				class="inline-flex shrink-0 items-center gap-2 rounded-md border px-3 py-2 text-sm disabled:opacity-60"
				style="border-color: var(--color-border)"
			>
				<Check size={15} aria-hidden="true" />
				Mark {newCount} seen
			</button>
		{/if}
	</header>

	{#if items.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<Rss size={20} aria-hidden="true" class="mx-auto" style="color: var(--color-muted)" />
			<p class="mt-2 font-medium">Nothing here yet.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				{#if followCount === 0}
					<a href="/discover" class="underline">Find something to follow</a> — a playlist, or
					everything someone publishes.
				{:else}
					Nothing new has been added to what you follow.
				{/if}
			</p>
		</div>
	{:else}
		<ul class="mt-6 flex flex-col">
			{#each items as item (item.itemId)}
				<li
					class="border-t py-3 first:border-t-0"
					style="border-color: var(--color-border)"
				>
					<div class="flex items-baseline gap-2">
						{#if isNew(item)}
							<!-- Against the reader's own last look, not a fixed window. -->
							<span
								class="mt-1 inline-block size-1.5 shrink-0 rounded-full"
								style="background: var(--color-accent)"
								title="New since you last looked"
							></span>
						{/if}
						<div class="min-w-0 flex-1">
							<a href={item.url} target="_blank" rel="noreferrer" class="break-words hover:underline">
								{item.title ?? item.url}
							</a>
							<KindBadge kind={item.kind} />
							{#if item.wordCount}
								<a
									href={`/read/${item.linkId}`}
									class="ml-1.5 align-middle text-xs hover:underline"
									style="color: var(--color-muted)"
								>{readingLabel(item.wordCount)}</a>
							{/if}
							<p class="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs" style="color: var(--color-muted)">
								<a
									href={`/public/${encodeURIComponent(item.ownerUsername)}/${encodeURIComponent(item.playlistSlug)}`}
									class="hover:underline"
								>{item.playlistName}</a>
								<span aria-hidden="true">·</span>
								<span>@{item.ownerUsername}</span>
								<span aria-hidden="true">·</span>
								<span>{item.host}</span>
								<span aria-hidden="true">·</span>
								<span>{when(item.addedAt)}</span>
							</p>
						</div>
					</div>
				</li>
			{/each}
		</ul>

		{#if nextCursor}
			<div class="mt-4 text-center">
				<button
					type="button"
					onclick={loadMore}
					disabled={loadingMore}
					class="rounded-md border p-1.5 disabled:opacity-60"
					style="border-color: var(--color-border)"
					title="Load more"
					aria-label="Load more"
				>
					<ChevronDown size={18} aria-hidden="true" />
				</button>
			</div>
		{/if}
	{/if}

	{#if followCount > 0}
		<div class="mt-10">
			<h2 class="text-sm font-medium" style="color: var(--color-muted)">Following</h2>
			<div class="mt-2 flex flex-wrap gap-1.5 text-xs">
				{#each data.following.playlists as playlist (playlist.playlistId)}
					<a
						href={`/public/${encodeURIComponent(playlist.ownerUsername)}/${encodeURIComponent(playlist.slug)}`}
						class="rounded-full border px-2 py-0.5 hover:border-[var(--color-accent)]"
						style="border-color: var(--color-border)"
					>{playlist.name}</a>
				{/each}
				{#each data.following.users as user (user.username)}
					<a
						href={`/public/${encodeURIComponent(user.username)}`}
						class="rounded-full border px-2 py-0.5 hover:border-[var(--color-accent)]"
						style="border-color: var(--color-border)"
					>@{user.username}</a>
				{/each}
			</div>
		</div>
	{/if}
</section>
