<script lang="ts">
	import { Lock, EyeOff, Globe } from '@lucide/svelte';
	import NsfwBadge from './NsfwBadge.svelte';
	import type { Playlist } from '$lib/types';

	let { playlist }: { playlist: Playlist } = $props();
</script>

<a
	href={`/playlists/${playlist.id}`}
	class="flex flex-col gap-2 rounded-lg border p-4 transition-colors hover:border-[var(--color-accent)]"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<div class="flex items-start justify-between gap-2">
		<span class="font-medium">{playlist.name}</span>
		<span class="flex shrink-0 items-center gap-1">
			{#if playlist.nsfw}<NsfwBadge />{/if}
			<span
				class="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs"
				style="border-color: var(--color-border); color: var(--color-muted)"
			>
				{#if playlist.visibility === 'Private'}<Lock size={11} aria-hidden="true" />
				{:else if playlist.visibility === 'Unlisted'}<EyeOff size={11} aria-hidden="true" />
				{:else}<Globe size={11} aria-hidden="true" />{/if}{playlist.visibility}
			</span>
		</span>
	</div>

	{#if playlist.description}
		<p class="line-clamp-2 text-sm" style="color: var(--color-muted)">{playlist.description}</p>
	{/if}

	{#if playlist.tags.length}
		<div class="flex flex-wrap gap-1">
			{#each playlist.tags as tag (tag)}
				<span class="rounded px-1.5 py-0.5 text-xs" style="background: var(--color-bg); color: var(--color-muted)">
					{tag}
				</span>
			{/each}
		</div>
	{/if}

	<div class="mt-auto text-right text-xs" style="color: var(--color-muted)">
		{playlist.itemCount} {playlist.itemCount === 1 ? 'link' : 'links'}
	</div>
</a>
