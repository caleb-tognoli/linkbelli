<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Trash2 } from '@lucide/svelte';
	import FolderCard from '$lib/components/FolderCard.svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import SaveToFolderDialog from '$lib/components/SaveToFolderDialog.svelte';
	import NewFolderDialog from '$lib/components/NewFolderDialog.svelte';
	import NewPlaylistDialog from '$lib/components/NewPlaylistDialog.svelte';
	import MoveFolderDialog from '$lib/components/MoveFolderDialog.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state(false);

	const folder = $derived(data.folder);
	let folderName = $state(data.folder.name);

	async function saveName(el: HTMLInputElement) {
		const name = el.value.trim();
		if (!name) { el.value = folderName; return; }
		if (name === folderName) return;
		const res = await api.patch(`/folders/${folder.id}`, { name });
		if (res.ok) { folderName = name; await invalidateAll(); }
		else el.value = folderName;
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

<svelte:head><title>{data.folder.name} - linkbelli</title></svelte:head>

<section class="mx-auto max-w-5xl">
	<!-- Breadcrumb trail: Playlists / ancestors / current -->
	<nav class="flex flex-wrap items-center gap-1 text-sm" style="color: var(--color-muted)">
		<a href="/playlists" class="hover:underline">Playlists</a>
		{#each folder.breadcrumbs as crumb (crumb.id)}
			<span>/</span>
			<a href={`/folders/${crumb.id}`} class="hover:underline">{crumb.name}</a>
		{/each}
		<span>/</span>
		<span style="color: var(--color-text)">{folderName}</span>
	</nav>

	<header class="mt-3 flex flex-wrap items-center justify-between gap-3">
		<div class="min-w-0 flex-1">
			<!-- The heading carries the name for the page outline; the field carries the editing.
			     A field alone has no accessible name and leaves the page without an h1. -->
			<h1 class="sr-only">{folderName}</h1>
			<input
				type="text"
				value={folderName}
				aria-label="Folder name"
				title="Rename this folder"
				class="-mx-1 w-full min-w-0 rounded-control border border-transparent bg-transparent px-1 py-0.5 text-2xl font-semibold hover:border-border-strong focus:border-accent"
				onblur={(e) => saveName(e.currentTarget)}
				onkeydown={(e) => { if (e.key === 'Enter') e.currentTarget.blur(); if (e.key === 'Escape') { e.currentTarget.value = folderName; e.currentTarget.blur(); } }}
			/>
		</div>
		<div class="flex shrink-0 flex-wrap gap-2 text-sm">
			<NewPlaylistDialog folderId={folder.id} />
			<NewFolderDialog parentId={folder.id} variant="ghost" iconOnly />
			<MoveFolderDialog folderId={folder.id} currentParentId={folder.parentId} />
			<button type="button" onclick={remove} disabled={busy} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" style="color: var(--color-danger)" title="Delete folder" aria-label="Delete folder">
				<Trash2 size={17} aria-hidden="true" />
			</button>
		</div>
	</header>

	{#if folder.subfolders.length}
		<div class="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each folder.subfolders as sub (sub.id)}
				<FolderCard folder={sub} />
			{/each}
		</div>
	{/if}

	{#if folder.playlists.length === 0}
		<div class="mt-3 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">No playlists in this folder.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Use "Save to folder" on a playlist to file it here.
			</p>
		</div>
	{:else}
		<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each folder.playlists as entry (entry.playlistId)}
				{@const fromParams = `?from=${encodeURIComponent(`/folders/${folder.id}`)}&fromLabel=${encodeURIComponent(folder.name)}`}
				<PlaylistCard
					href={entry.ownedByMe
						? `/playlists/${entry.playlistId}${fromParams}`
						: `/public/${encodeURIComponent(entry.ownerUsername)}/${encodeURIComponent(entry.slug)}${fromParams}`}
					name={entry.name}
					description={entry.description}
					tags={entry.tags}
					nsfw={entry.nsfw}
					visibility={entry.ownedByMe ? entry.visibility : undefined}
					owner={entry.ownedByMe ? undefined : entry.ownerUsername}
					itemCount={entry.itemCount}
				>
					{#snippet actions()}
						<SaveToFolderDialog
							playlistId={entry.playlistId}
							currentFolderId={folder.id}
							currentFolderName={folder.name}
							compact
						/>
					{/snippet}
				</PlaylistCard>
			{/each}
		</div>
	{/if}
</section>
