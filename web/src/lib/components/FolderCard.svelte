<script lang="ts">
	import type { Folder } from '$lib/types';

	let { folder }: { folder: Folder } = $props();

	const subLabel = $derived(
		`${folder.subfolderCount} ${folder.subfolderCount === 1 ? 'folder' : 'folders'}`
	);
	const plLabel = $derived(
		`${folder.playlistCount} ${folder.playlistCount === 1 ? 'playlist' : 'playlists'}`
	);
</script>

<a
	href={`/folders/${folder.id}`}
	class="flex items-center gap-3 rounded-lg border p-4 transition-colors hover:border-[var(--color-accent)]"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="color: var(--color-muted)">
		<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
	</svg>
	<div class="min-w-0 flex-1">
		<div class="truncate font-medium">{folder.name}</div>
		<div class="text-xs" style="color: var(--color-muted)">{subLabel} · {plLabel}</div>
	</div>
</a>
