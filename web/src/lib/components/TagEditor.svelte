<script lang="ts">
	import Chip from '$lib/components/ui/Chip.svelte';
	import { api } from '$lib/api/client';
	import { Tag } from '@lucide/svelte';
	import type { Playlist } from '$lib/types';

	let {
		playlistId,
		tags = $bindable(),
		readonly = false
	}: { playlistId: string; tags: string[]; readonly?: boolean } = $props();

	let input = $state('');
	let busy = $state(false);

	async function save(next: string[]) {
		busy = true;
		try {
			const res = await api.patch(`/playlists/${playlistId}`, { tags: next });
			if (res.ok) tags = ((await res.json()) as Playlist).tags;
		} finally {
			busy = false;
		}
	}

	function addTag() {
		const t = input.trim();
		input = '';
		if (t) save([...tags, t]);
	}

	function removeTag(tag: string) {
		save(tags.filter((t) => t !== tag));
	}
</script>

<div class="flex flex-wrap items-center gap-1.5">
	{#each tags as tag (tag)}
		<Chip
			onremove={readonly ? undefined : () => removeTag(tag)}
			removeLabel={`Remove tag ${tag}`}
			disabled={busy}
		>
			{tag}
		</Chip>
	{/each}
	{#if !readonly}
		<span class="inline-flex items-center gap-1" style="color: var(--color-muted)">
			<Tag size={14} aria-hidden="true" />
			<input
				bind:value={input}
				placeholder="add tag…"
				aria-label="Add tag"
				disabled={busy}
				class="w-20 border-0 bg-transparent text-xs outline-none"
				style="color: var(--color-text)"
				onkeydown={(e) => {
					if (e.key === 'Enter') {
						e.preventDefault();
						addTag();
					}
				}}
				onblur={addTag}
			/>
		</span>
	{/if}
</div>
