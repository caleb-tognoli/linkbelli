<script lang="ts">
	import { api } from '$lib/api/client';
	import type { Playlist } from '$lib/types';

	let { playlistId, tags = $bindable() }: { playlistId: string; tags: string[] } = $props();

	let input = $state('');
	let busy = $state(false);

	async function save(next: string[]) {
		busy = true;
		try {
			const res = await api.patch(`/playlists/${playlistId}`, { tags: next });
			if (res.ok) tags = ((await res.json()) as Playlist).tags; // reflect server normalization
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
		<span
			class="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs"
			style="background: var(--color-surface); color: var(--color-muted)"
		>
			#{tag}
			<button type="button" onclick={() => removeTag(tag)} title="Remove tag" disabled={busy}>×</button>
		</span>
	{/each}
	<input
		bind:value={input}
		placeholder="add tag…"
		disabled={busy}
		class="w-24 rounded border px-1.5 py-0.5 text-xs"
		style="border-color: var(--color-border); background: var(--color-bg)"
		onkeydown={(e) => {
			if (e.key === 'Enter') {
				e.preventDefault();
				addTag();
			}
		}}
		onblur={addTag}
	/>
</div>
