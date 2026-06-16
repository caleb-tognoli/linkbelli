<script lang="ts">
	import { api } from '$lib/api/client';
	import { ChevronDown } from '@lucide/svelte';
	import NsfwBadge from '$lib/components/NsfwBadge.svelte';
	import SaveToFolderDialog from '$lib/components/SaveToFolderDialog.svelte';
	import type { Paged, PlaylistItem } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let items = $state(data.items.items);
	let nextCursor = $state(data.items.nextCursor);
	let loadingMore = $state(false);

	// Reset when navigating to a different public playlist (same route, no remount).
	let seen = data.items;
	$effect(() => {
		if (data.items !== seen) {
			seen = data.items;
			items = data.items.items;
			nextCursor = data.items.nextCursor;
		}
	});

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const base = `/public/playlists/${encodeURIComponent(data.username)}/${encodeURIComponent(data.slug)}/items`;
			const res = await api.get(`${base}?cursor=${encodeURIComponent(nextCursor)}`);
			if (res.ok) {
				const page = (await res.json()) as Paged<PlaylistItem>;
				items = [...items, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}

	function added(iso: string) {
		return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
	}
</script>

<section class="mx-auto max-w-4xl">
	<a href="/discover" class="text-sm" style="color: var(--color-muted)">← Discover</a>

	<header class="mt-3">
		<div class="flex flex-wrap items-start justify-between gap-3">
			<h1 class="text-2xl font-semibold">{data.playlist.name}</h1>
			{#if data.user}
				<SaveToFolderDialog
					playlistId={data.playlist.id}
					currentFolderId={data.playlist.folderId}
					currentFolderName={data.playlist.folderName}
				/>
			{/if}
		</div>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">by @{data.username}</p>
		{#if data.playlist.description}
			<p class="mt-2" style="color: var(--color-muted)">{data.playlist.description}</p>
		{/if}
		{#if data.playlist.tags.length}
			<div class="mt-3 flex flex-wrap gap-1">
				{#each data.playlist.tags as tag (tag)}
					<span class="rounded px-1.5 py-0.5 text-xs" style="background: var(--color-surface); color: var(--color-muted)">{tag}</span>
				{/each}
			</div>
		{/if}
	</header>

	{#if items.length === 0}
		<p class="mt-8 text-sm" style="color: var(--color-muted)">This playlist has no links yet.</p>
	{:else}
		<div class="mt-6 overflow-x-auto">
		<table class="w-full border-collapse text-sm">
			<thead>
				<tr class="text-left" style="color: var(--color-muted)">
					<th class="py-2 font-medium">Title</th>
					<th class="py-2 font-medium">Domain</th>
					<th class="py-2 font-medium">Added</th>
				</tr>
			</thead>
			<tbody>
				{#each items as item (item.id)}
					<tr class="border-t" style="border-color: var(--color-border)">
						<td class="py-2 pr-3">
							<a href={item.link.url} target="_blank" rel="noopener noreferrer" class="hover:underline">
								{item.link.title ?? item.link.url}
							</a>
							{#if item.link.nsfw}<NsfwBadge />{/if}
						</td>
						<td class="py-2 pr-3" style="color: var(--color-muted)">{item.link.host}</td>
						<td class="py-2" style="color: var(--color-muted)">{added(item.creationTime)}</td>
					</tr>
				{/each}
			</tbody>
		</table>
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
