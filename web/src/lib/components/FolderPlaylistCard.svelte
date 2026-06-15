<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import NsfwBadge from './NsfwBadge.svelte';
	import type { FolderPlaylistEntry } from '$lib/types';

	let { entry, folderId }: { entry: FolderPlaylistEntry; folderId: string } = $props();

	let busy = $state(false);

	// Own playlists open the editable view; saved public ones deep-link to the public page.
	const href = $derived(
		entry.ownedByMe
			? `/playlists/${entry.playlistId}`
			: `/public/${encodeURIComponent(entry.ownerUsername)}/${encodeURIComponent(entry.slug)}`
	);

	async function remove() {
		if (busy) return;
		const verb = entry.ownedByMe ? 'Remove this playlist from the folder?' : 'Remove this saved playlist?';
		if (!confirm(verb)) return;
		busy = true;
		try {
			const res = await api.del(`/folders/${folderId}/playlists/${entry.playlistId}`);
			if (res.ok || res.status === 204) await invalidateAll();
		} finally {
			busy = false;
		}
	}
</script>

<div
	class="flex flex-col gap-2 rounded-lg border p-4"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<div class="flex items-start justify-between gap-2">
		<a href={href} class="min-w-0 flex-1 font-medium hover:underline">{entry.name}</a>
		<span class="flex shrink-0 items-center gap-1">
			{#if entry.nsfw}<NsfwBadge />{/if}
			{#if !entry.ownedByMe}
				<span class="rounded-full border px-2 py-0.5 text-xs" style="border-color: var(--color-border); color: var(--color-muted)">
					@{entry.ownerUsername}
				</span>
			{/if}
		</span>
	</div>

	{#if entry.description}
		<p class="line-clamp-2 text-sm" style="color: var(--color-muted)">{entry.description}</p>
	{/if}

	{#if entry.tags.length}
		<div class="flex flex-wrap gap-1">
			{#each entry.tags as tag (tag)}
				<span class="rounded px-1.5 py-0.5 text-xs" style="background: var(--color-bg); color: var(--color-muted)">{tag}</span>
			{/each}
		</div>
	{/if}

	<div class="mt-auto flex items-center justify-between text-xs" style="color: var(--color-muted)">
		<span>{entry.itemCount} {entry.itemCount === 1 ? 'link' : 'links'}</span>
		<button type="button" onclick={remove} disabled={busy} class="hover:underline disabled:opacity-60" style="color: var(--color-danger)">
			Remove
		</button>
	</div>
</div>
