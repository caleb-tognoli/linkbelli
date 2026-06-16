<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog, promptDialog } from '$lib/dialog.svelte';
	import { Pencil, Trash2 } from '@lucide/svelte';
	import FolderCard from '$lib/components/FolderCard.svelte';
	import FolderPlaylistCard from '$lib/components/FolderPlaylistCard.svelte';
	import NewFolderDialog from '$lib/components/NewFolderDialog.svelte';
	import MoveFolderDialog from '$lib/components/MoveFolderDialog.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state(false);

	const folder = $derived(data.folder);

	async function rename() {
		const next = await promptDialog('Folder name', folder.name);
		if (next === null) return;
		const name = next.trim();
		if (!name || name === folder.name) return;
		busy = true;
		try {
			const res = await api.patch(`/folders/${folder.id}`, { name });
			if (res.ok) await invalidateAll();
		} finally {
			busy = false;
		}
	}

	async function remove() {
		const msg =
			folder.subfolders.length || folder.playlists.length
				? 'Delete this folder and everything inside it? Your playlists themselves are not deleted.'
				: 'Delete this folder?';
		if (!(await confirmDialog(msg, { danger: true, confirmLabel: 'Delete' }))) return;
		busy = true;
		try {
			const res = await api.del(`/folders/${folder.id}`);
			if (res.ok || res.status === 204) {
				await goto(folder.parentId ? `/folders/${folder.parentId}` : '/');
			} else {
				busy = false;
			}
		} catch {
			busy = false;
		}
	}
</script>

<section class="mx-auto max-w-5xl">
	<!-- Breadcrumb trail: Home / ancestors / current -->
	<nav class="flex flex-wrap items-center gap-1 text-sm" style="color: var(--color-muted)">
		<a href="/" class="hover:underline">Home</a>
		{#each folder.breadcrumbs as crumb (crumb.id)}
			<span>/</span>
			<a href={`/folders/${crumb.id}`} class="hover:underline">{crumb.name}</a>
		{/each}
		<span>/</span>
		<span style="color: var(--color-text)">{folder.name}</span>
	</nav>

	<header class="mt-3 flex flex-wrap items-center justify-between gap-3">
		<div class="flex items-center gap-3">
			<h1 class="text-2xl font-semibold">{folder.name}</h1>
			<button type="button" onclick={rename} disabled={busy} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" title="Rename folder" aria-label="Rename folder">
				<Pencil size={17} aria-hidden="true" />
			</button>
		</div>
		<div class="flex shrink-0 flex-wrap gap-2 text-sm">
			<NewFolderDialog
				parentId={folder.id}
				label=""
				triggerClass="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				triggerStyle=""
			/>
			<MoveFolderDialog folderId={folder.id} currentParentId={folder.parentId} />
			<button type="button" onclick={remove} disabled={busy} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" style="color: var(--color-danger)" title="Delete folder" aria-label="Delete folder">
				<Trash2 size={17} aria-hidden="true" />
			</button>
		</div>
	</header>

	{#if folder.subfolders.length}
		<h2 class="mt-8 text-sm font-medium" style="color: var(--color-muted)">Subfolders</h2>
		<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each folder.subfolders as sub (sub.id)}
				<FolderCard folder={sub} />
			{/each}
		</div>
	{/if}

	<h2 class="mt-8 text-sm font-medium" style="color: var(--color-muted)">Playlists</h2>
	{#if folder.playlists.length === 0}
		<div class="mt-3 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">No playlists in this folder.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Use “Save to folder” on a playlist to file it here.
			</p>
		</div>
	{:else}
		<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each folder.playlists as entry (entry.playlistId)}
				<FolderPlaylistCard {entry} folderId={folder.id} folderName={folder.name} />
			{/each}
		</div>
	{/if}
</section>
