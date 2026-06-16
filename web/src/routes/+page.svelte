<script lang="ts">
	import { Plus } from '@lucide/svelte';
	import FolderCard from '$lib/components/FolderCard.svelte';
	import NewFolderDialog from '$lib/components/NewFolderDialog.svelte';
	import NewPlaylistDialog from '$lib/components/NewPlaylistDialog.svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import SourcesList from '$lib/components/SourcesList.svelte';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	const showFolders = $derived(data.rootFolders.length > 0);
	const isEmpty = $derived(data.playlists.items.length === 0 && !showFolders);
</script>

<div class="mx-auto flex max-w-5xl flex-col gap-12">
	<section id="playlists" class="scroll-mt-6">
		<header class="flex items-center justify-between gap-2">
			<h1 class="text-2xl font-semibold">Playlists</h1>
			<div class="flex shrink-0 items-center gap-1">
				<NewFolderDialog
					label=""
					triggerClass="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					triggerStyle=""
				/>
				<NewPlaylistDialog {form} />
			</div>
		</header>

		{#if isEmpty}
			<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
				<p class="font-medium">No folders or playlists yet.</p>
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
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="New source"
				aria-label="New source"
			>
				<Plus size={18} aria-hidden="true" />
			</a>
		</header>
		<div class="mt-6">
			<SourcesList sources={data.sources} />
		</div>
	</section>
</div>
