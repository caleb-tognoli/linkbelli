<script lang="ts">
	import { dndzone } from 'svelte-dnd-action';
	import { api } from '$lib/api/client';
	import NsfwBadge from './NsfwBadge.svelte';
	import type { PlaylistItem } from '$lib/types';

	let { items = $bindable() }: { items: PlaylistItem[] } = $props();

	let editingId = $state<string | null>(null);
	let draftNote = $state('');

	const FLIP = 150;

	function onConsider(e: CustomEvent<{ items: PlaylistItem[] }>) {
		items = e.detail.items;
	}

	async function onFinalize(e: CustomEvent<{ items: PlaylistItem[]; info: { id: string } }>) {
		items = e.detail.items;
		const movedId = e.detail.info.id;
		const idx = items.findIndex((i) => i.id === movedId);
		const afterItemId = idx > 0 ? items[idx - 1].id : null;
		await api.post(`/items/${movedId}/move`, { afterItemId });
	}

	function startEdit(item: PlaylistItem) {
		editingId = item.id;
		draftNote = item.note ?? '';
	}

	async function saveNote(item: PlaylistItem) {
		const note = draftNote.trim() || null;
		editingId = null;
		const res = await api.patch(`/items/${item.id}`, { note });
		if (res.ok) items = items.map((i) => (i.id === item.id ? { ...i, note } : i));
	}

	async function remove(item: PlaylistItem) {
		const res = await api.del(`/items/${item.id}`);
		if (res.ok || res.status === 204) items = items.filter((i) => i.id !== item.id);
	}

	function added(iso: string) {
		return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
	}
</script>

{#if items.length === 0}
	<div
		class="rounded-lg border border-dashed p-10 text-center"
		style="border-color: var(--color-border)"
	>
		<p class="font-medium">No links yet.</p>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">Paste a URL above to add the first.</p>
	</div>
{:else}
	<div class="overflow-x-auto">
	<table class="w-full border-collapse text-sm">
		<thead>
			<tr class="text-left" style="color: var(--color-muted)">
				<th class="w-6"></th>
				<th class="py-2 font-medium">Title</th>
				<th class="py-2 font-medium">Domain</th>
				<th class="py-2 font-medium">Added</th>
				<th class="py-2 font-medium">Note</th>
				<th class="w-10"></th>
			</tr>
		</thead>
		<tbody
			use:dndzone={{ items, flipDurationMs: FLIP, dropTargetStyle: {} }}
			onconsider={onConsider}
			onfinalize={onFinalize}
		>
			{#each items as item (item.id)}
				<tr class="border-t align-top" style="border-color: var(--color-border)">
					<td class="cursor-grab select-none py-2 pr-1" style="color: var(--color-muted)" title="Drag to reorder">
						<span role="img" aria-label="Drag to reorder">⋮⋮</span>
					</td>
					<td class="py-2 pr-3">
						<a href={item.link.url} target="_blank" rel="noopener noreferrer" class="hover:underline">
							{item.link.title ?? item.link.url}
						</a>
						{#if item.link.nsfw}<NsfwBadge />{/if}
						{#if !item.link.enriched}
							<span class="ml-1 text-xs" style="color: var(--color-muted)" title="Fetching metadata…">·</span>
						{/if}
					</td>
					<td class="py-2 pr-3" style="color: var(--color-muted)">{item.link.host}</td>
					<td class="py-2 pr-3" style="color: var(--color-muted)">{added(item.creationTime)}</td>
					<td class="py-2 pr-3">
						{#if editingId === item.id}
							<input
								bind:value={draftNote}
								class="w-full rounded border px-2 py-1 text-sm"
								style="border-color: var(--color-border); background: var(--color-bg)"
								onblur={() => saveNote(item)}
								onkeydown={(e) => {
									if (e.key === 'Enter') saveNote(item);
									if (e.key === 'Escape') editingId = null;
								}}
							/>
						{:else}
							<button
								type="button"
								class="text-left"
								style={item.note ? '' : 'color: var(--color-muted)'}
								onclick={() => startEdit(item)}
							>
								{item.note ?? 'Add note'}
							</button>
						{/if}
					</td>
					<td class="py-2 text-right">
						<button
							type="button"
							onclick={() => remove(item)}
							class="rounded px-1.5 py-0.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
							style="color: var(--color-danger)"
							title="Remove"
						>
							✕
						</button>
					</td>
				</tr>
			{/each}
		</tbody>
	</table>
	</div>
{/if}
