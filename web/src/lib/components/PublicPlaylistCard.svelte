<script lang="ts">
	import NsfwBadge from './NsfwBadge.svelte';
	import type { PublicPlaylistSummary } from '$lib/types';

	let { playlist }: { playlist: PublicPlaylistSummary } = $props();
</script>

<a
	href={`/public/${encodeURIComponent(playlist.ownerUsername)}/${encodeURIComponent(playlist.slug)}`}
	class="flex flex-col gap-2 rounded-lg border p-4 transition-colors hover:border-[var(--color-accent)]"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<div class="flex items-start justify-between gap-2">
		<span class="font-medium">{playlist.name}</span>
		{#if playlist.nsfw}<span class="shrink-0"><NsfwBadge /></span>{/if}
	</div>

	{#if playlist.description}
		<p class="line-clamp-2 text-sm" style="color: var(--color-muted)">{playlist.description}</p>
	{/if}

	{#if playlist.tags.length}
		<div class="flex flex-wrap gap-1">
			{#each playlist.tags as tag (tag)}
				<span class="rounded px-1.5 py-0.5 text-xs" style="background: var(--color-bg); color: var(--color-muted)">{tag}</span>
			{/each}
		</div>
	{/if}

	<div class="mt-auto flex justify-between text-xs" style="color: var(--color-muted)">
		<span>@{playlist.ownerUsername}</span>
		<span>{playlist.itemCount} {playlist.itemCount === 1 ? 'link' : 'links'}</span>
	</div>
</a>
