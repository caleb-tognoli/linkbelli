<script lang="ts">
	import FolderCard from '$lib/components/FolderCard.svelte';
	import NewFolderDialog from '$lib/components/NewFolderDialog.svelte';
	import NewPlaylistDialog from '$lib/components/NewPlaylistDialog.svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import SourcesList from '$lib/components/SourcesList.svelte';
	import TagFilter from '$lib/components/TagFilter.svelte';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	// Folders are an organization aid for browsing; hide them while filtering by tag so the
	// filtered result set (root playlists matching the tag) stays the focus.
	const showFolders = $derived(data.rootFolders.length > 0 && data.activeTags.length === 0);
	const isEmpty = $derived(data.playlists.items.length === 0 && !showFolders);
</script>

<div class="mx-auto flex max-w-5xl flex-col gap-12">
	<section id="playlists" class="scroll-mt-6">
		<header class="flex items-center justify-between gap-2">
			<h1 class="text-2xl font-semibold">Playlists</h1>
			<div class="flex shrink-0 gap-2">
				<NewFolderDialog
					label="New folder"
					triggerClass="rounded-md border px-3 py-2 text-sm font-medium"
				/>
				<NewPlaylistDialog {form} />
			</div>
		</header>

		<div class="mt-4">
			<TagFilter active={data.activeTags} basePath="/" suggestPath="/tags" />
		</div>

		{#if isEmpty}
			<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
				<p class="font-medium">
					{data.activeTags.length ? 'No playlists match those tags.' : 'No folders or playlists yet.'}
				</p>
				<p class="mt-1 text-sm" style="color: var(--color-muted)">
					Create a folder to organize things, or a playlist to start collecting links.
				</p>
			</div>
		{:else}
			{#if showFolders}
				<div class="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
					{#each data.rootFolders as folder (folder.id)}
						<FolderCard {folder} />
					{/each}
				</div>
			{/if}

			{#if data.playlists.items.length}
				<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
					{#each data.playlists.items as playlist (playlist.id)}
						<PlaylistCard {playlist} />
					{/each}
				</div>
			{/if}
		{/if}
	</section>

	<section id="sources" class="scroll-mt-6">
		<header class="flex items-center justify-between">
			<h2 class="text-2xl font-semibold">Sources</h2>
			<a
				href="/sources/new"
				class="rounded-md px-3 py-2 text-sm font-medium"
				style="background: var(--color-accent); color: var(--color-accent-contrast)"
			>
				New source
			</a>
		</header>
		<div class="mt-6">
			<SourcesList sources={data.sources} />
		</div>
	</section>
</div>
