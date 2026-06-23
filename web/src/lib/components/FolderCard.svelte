<script lang="ts">
	import { Folder as FolderIcon } from '@lucide/svelte';
	import type { Folder } from '$lib/types';

	let { folder }: { folder: Folder } = $props();

	const subLabel = $derived(
		folder.subfolderCount > 0
			? `${folder.subfolderCount} ${folder.subfolderCount === 1 ? 'folder' : 'folders'}`
			: null
	);
	const plLabel = $derived(
		folder.playlistCount > 0
			? `${folder.playlistCount} ${folder.playlistCount === 1 ? 'playlist' : 'playlists'}`
			: null
	);
</script>

<a
	href={`/folders/${folder.id}`}
	class="flex items-center gap-3 rounded-lg border p-4 transition-colors hover:border-[var(--color-accent)]"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<FolderIcon size={23} aria-hidden="true" style="color: var(--color-muted)" />
	<div class="min-w-0 flex-1">
		<div class="truncate font-medium">{folder.name}</div>
		<div class="text-xs" style="color: var(--color-muted)">
			{#if subLabel && plLabel}{subLabel} · {plLabel}{:else if subLabel}{subLabel}{:else if plLabel}{plLabel}{:else}Empty{/if}
		</div>
	</div>
</a>
