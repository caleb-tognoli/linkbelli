<script lang="ts">
	import { pageTitle } from '$lib/title';
	import { formatDate } from '$lib/dates';
	import Button from '$lib/components/ui/Button.svelte';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
	import Chip from '$lib/components/ui/Chip.svelte';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { toast } from '$lib/toast.svelte';
	import { failureMessage } from '$lib/api/errors';
	import KindBadge from '$lib/components/KindBadge.svelte';
	import { readingLabel } from '$lib/reading';
	import { Check, Rss, Compass } from '@lucide/svelte';
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
			} else {
				toast.error(failureMessage(res.status, 'Could not load any more.'));
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

	const when = formatDate;
</script>

<svelte:head><title>{pageTitle('Feed')}</title></svelte:head>

<Page width="narrow">
	<PageHeader title="Feed">
		{#snippet details()}
			{#if followCount === 0}
				<!-- Said once. The empty box below says the same thing, and both at once read as a
				     page apologising twice. -->
				What you follow turns up here.
			{:else}
				New links from {followCount} {followCount === 1 ? 'playlist or person' : 'playlists and people'} you follow.
			{/if}
		{/snippet}
		{#snippet actions()}
			{#if newCount > 0}
				<Button icon={Check} onclick={markSeen} loading={marking}>Mark {newCount} seen</Button>
			{/if}
		{/snippet}
	</PageHeader>

	{#if items.length === 0}
		<div class="mt-8 rounded-card border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<Rss size={20} aria-hidden="true" class="mx-auto" style="color: var(--color-muted)" />
			<p class="mt-2 font-medium">Nothing here yet.</p>
			<p class="mt-1 text-sm text-muted">
				{#if followCount === 0}
					Follow a playlist, or everything someone publishes.
				{:else}
					Nothing new has been added to what you follow.
				{/if}
			</p>
			{#if followCount === 0}
				<div class="mt-4 flex justify-center">
					<Button href="/discover" variant="primary" icon={Compass}>Find something to follow</Button>
				</div>
			{/if}
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
								aria-hidden="true"
							></span>
							<span class="sr-only">New:</span>
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
			<LoadMore onclick={loadMore} loading={loadingMore} />
		{/if}
	{/if}

	{#if followCount > 0}
		<div class="mt-10">
			<h2 class="t-subsection">Following</h2>
			<div class="mt-2 flex flex-wrap gap-1.5 text-xs">
				{#each data.following.playlists as playlist (playlist.playlistId)}
					<Chip href={`/public/${encodeURIComponent(playlist.ownerUsername)}/${encodeURIComponent(playlist.slug)}`}>
						{playlist.name}
					</Chip>
				{/each}
				{#each data.following.users as user (user.username)}
					<Chip href={`/public/${encodeURIComponent(user.username)}`}>@{user.username}</Chip>
				{/each}
			</div>
		</div>
	{/if}
</Page>
