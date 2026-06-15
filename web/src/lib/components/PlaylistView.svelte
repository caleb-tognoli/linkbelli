<script lang="ts">
	import AddLinkBar from './AddLinkBar.svelte';
	import LinkTable from './LinkTable.svelte';
	import TagEditor from './TagEditor.svelte';
	import SourcesPanel from './SourcesPanel.svelte';
	import SaveToFolderDialog from './SaveToFolderDialog.svelte';
	import { api } from '$lib/api/client';
	import type { AttachedSource, Paged, Playlist, PlaylistItem, SourceSummary } from '$lib/types';

	let {
		playlist,
		items: itemsPage,
		attachedSources,
		ownSources
	}: {
		playlist: Playlist;
		items: Paged<PlaylistItem>;
		attachedSources: AttachedSource[];
		ownSources: SourceSummary[];
	} = $props();

	let items = $state(itemsPage.items);
	let nextCursor = $state(itemsPage.nextCursor);
	let tags = $state(playlist.tags);
	let attached = $state(attachedSources);
	let loadingMore = $state(false);

	function onAdded(item: PlaylistItem) {
		items = [...items, item];
	}

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const res = await api.get(
				`/playlists/${playlist.id}/items?cursor=${encodeURIComponent(nextCursor)}`
			);
			if (res.ok) {
				const page = (await res.json()) as Paged<PlaylistItem>;
				items = [...items, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}
</script>

<section class="mx-auto max-w-4xl">
	<a href="/" class="text-sm" style="color: var(--color-muted)">← Playlists</a>

	<header class="mt-3 flex items-start justify-between gap-3">
		<div>
			<h1 class="text-2xl font-semibold">{playlist.name}</h1>
			{#if playlist.description}
				<p class="mt-1" style="color: var(--color-muted)">{playlist.description}</p>
			{/if}
		</div>
		<div class="flex shrink-0 items-center gap-2">
			<SaveToFolderDialog
				playlistId={playlist.id}
				currentFolderId={playlist.folderId}
				currentFolderName={playlist.folderName}
			/>
			<span
				class="rounded-full border px-2 py-0.5 text-xs"
				style="border-color: var(--color-border); color: var(--color-muted)"
			>
				{playlist.visibility}
			</span>
		</div>
	</header>

	<div class="mt-3">
		<TagEditor playlistId={playlist.id} bind:tags />
	</div>

	<div class="mt-5">
		<AddLinkBar playlistId={playlist.id} {onAdded} />
	</div>

	<div class="mt-5">
		<LinkTable bind:items />
		{#if nextCursor}
			<div class="mt-3 text-center">
				<button
					type="button"
					onclick={loadMore}
					disabled={loadingMore}
					class="rounded-md border px-3 py-1.5 text-sm disabled:opacity-60"
					style="border-color: var(--color-border)"
				>
					{loadingMore ? 'Loading…' : 'Load more'}
				</button>
			</div>
		{/if}
	</div>

	<div class="mt-6">
		<SourcesPanel playlistId={playlist.id} bind:attached {ownSources} />
	</div>
</section>
