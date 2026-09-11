<svelte:head><title>Playlists - linkbelli</title></svelte:head>

<script lang="ts">
	import FolderCard from '$lib/components/FolderCard.svelte';
	import NewFolderDialog from '$lib/components/NewFolderDialog.svelte';
	import NewPlaylistDialog from '$lib/components/NewPlaylistDialog.svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import { CopyCheck, Trash2 } from '@lucide/svelte';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	const showFolders = $derived(data.rootFolders.length > 0);
	const isEmpty = $derived(data.playlists.items.length === 0 && !showFolders);
</script>

<section class="mx-auto max-w-5xl">
	<header class="flex items-center justify-between gap-2">
		<h1 class="text-2xl font-semibold">Playlists</h1>
		<div class="flex shrink-0 items-center gap-1">
			<a
				href="/duplicates"
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="Duplicates"
				aria-label="Duplicates"
			>
				<CopyCheck size={18} aria-hidden="true" />
			</a>
			<a
				href="/trash"
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="Trash"
				aria-label="Trash"
			>
				<Trash2 size={18} aria-hidden="true" />
			</a>
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
				Create a playlist to start collecting links.
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
