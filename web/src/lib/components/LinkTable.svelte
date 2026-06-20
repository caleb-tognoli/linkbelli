<script lang="ts">
	import { Popover } from 'bits-ui';
	import { dndzone } from 'svelte-dnd-action';
	import { api } from '$lib/api/client';
	import { Clock, Image, Link2, Shuffle, StickyNote, Trash2 } from '@lucide/svelte';
	import NsfwBadge from './NsfwBadge.svelte';
	import type { PlaylistItem } from '$lib/types';

	let {
		items = $bindable(),
		readonly = false
	}: { items: PlaylistItem[]; readonly?: boolean } = $props();

	type SortMode = 'manual' | 'date-asc' | 'date-desc' | 'shuffle';
	let sortMode = $state<SortMode>(readonly ? 'date-desc' : 'manual');
	let shuffledItems = $state<PlaylistItem[]>([]);
	let showThumbnails = $state(true);
	let showUrls = $state(false);

	const sortOptions: { mode: SortMode; label: string }[] = [
		{ mode: 'manual', label: 'Manual' },
		{ mode: 'date-asc', label: 'Oldest' },
		{ mode: 'date-desc', label: 'Newest' },
		{ mode: 'shuffle', label: 'Shuffle' }
	];

	// Manual sort is owner-only (requires drag). Hide it for readonly viewers.
	const visibleSortOptions = $derived(
		readonly ? sortOptions.filter((o) => o.mode !== 'manual') : sortOptions
	);

	function setSort(mode: SortMode) {
		sortMode = mode;
		if (mode === 'shuffle') shuffledItems = [...items].sort(() => Math.random() - 0.5);
	}

	const displayItems = $derived(
		sortMode === 'manual'
			? items
			: sortMode === 'shuffle'
				? shuffledItems
				: [...items].sort((a, b) => {
						const diff = new Date(a.creationTime).getTime() - new Date(b.creationTime).getTime();
						return sortMode === 'date-asc' ? diff : -diff;
					})
	);

	let noteEditId = $state<string | null>(null);
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

	async function saveNote(item: PlaylistItem) {
		const note = draftNote.trim() || null;
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

	function isPending(item: PlaylistItem) {
		return item.status === 'Pending' || !item.link.enriched;
	}

	const toggleClass = 'rounded-full border px-2.5 py-0.5 text-xs transition-colors';
	function toggleStyle(active: boolean) {
		return active
			? 'border-color: var(--color-accent); color: var(--color-accent)'
			: 'border-color: var(--color-border); color: var(--color-muted)';
	}
</script>

{#snippet row(item: PlaylistItem, draggable: boolean)}
	<tr class="border-t align-middle" style="border-color: var(--color-border)">
		{#if !readonly}
			<td
				class="select-none pr-1"
				class:cursor-grab={draggable}
				style="color: var(--color-muted)"
				title={draggable ? 'Drag to reorder' : undefined}
			>
				{#if draggable}<span role="img" aria-label="Drag to reorder">⋮⋮</span>{/if}
			</td>
		{/if}
		<td class="py-2 pr-3">
			<div class="flex items-center gap-4">
				{#if showThumbnails && item.link.thumbnailUrl}
					<img
						src={item.link.thumbnailUrl}
						alt=""
						class="shrink-0 rounded object-cover"
						style="height: 5em; width: auto"
					/>
				{/if}
				<div class="min-w-0">
					<a
						href={item.link.url}
						target="_blank"
						rel="noopener noreferrer"
						class="break-words hover:underline"
					>
						{showUrls ? item.link.url : (item.link.title ?? item.link.url)}
					</a>
					{#if item.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}
					{#if item.note && readonly}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.note}</p>
					{/if}
				</div>
			</div>
		</td>
		<td class="whitespace-nowrap pr-3" style="color: var(--color-muted)">{added(item.creationTime)}</td>
		{#if !readonly}
			<td class="text-right">
				<Popover.Root
					onOpenChange={(o) => {
						if (o) {
							noteEditId = item.id;
							draftNote = item.note ?? '';
						} else if (noteEditId === item.id) {
							saveNote(item);
							noteEditId = null;
						}
					}}
				>
					<Popover.Trigger
						class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
						style={isPending(item)
							? 'color: var(--color-muted)'
							: item.note
								? 'color: var(--color-accent)'
								: 'color: var(--color-muted)'}
						title={item.note ? 'Edit note' : 'Add note'}
						aria-label={item.note ? 'Edit note' : 'Add note'}
					>
						{#if isPending(item)}
							<Clock size={16} aria-hidden="true" />
						{:else}
							<StickyNote size={16} aria-hidden="true" />
						{/if}
					</Popover.Trigger>
					<Popover.Content
						class="popover-surface z-30 w-64 rounded-lg border p-3 shadow-md"
						sideOffset={4}
					>
						<textarea
							bind:value={draftNote}
							placeholder="Add note…"
							rows={4}
							class="w-full resize-none rounded border px-2 py-1 text-sm"
							style="border-color: var(--color-border); background: var(--color-bg)"
							onkeydown={(e) => {
								if (e.key === 'Enter' && e.ctrlKey) saveNote(item);
							}}
							autofocus
						></textarea>
					</Popover.Content>
				</Popover.Root>
			</td>
			<td class="text-right">
				<button
					type="button"
					onclick={() => remove(item)}
					class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
					style="color: var(--color-danger)"
					title="Remove link"
					aria-label="Remove link"
				>
					<Trash2 size={16} aria-hidden="true" />
				</button>
			</td>
		{/if}
	</tr>
{/snippet}

{#if items.length === 0}
	<div
		class="rounded-lg border border-dashed p-10 text-center"
		style="border-color: var(--color-border)"
	>
		<p class="font-medium">No links yet.</p>
		{#if !readonly}
			<p class="mt-1 text-sm" style="color: var(--color-muted)">Paste a URL above to add the first.</p>
		{/if}
	</div>
{:else}
	<div class="mb-3 flex items-center gap-1.5">
		{#each visibleSortOptions as opt (opt.mode)}
			<button
				type="button"
				onclick={() => setSort(opt.mode)}
				class={toggleClass}
				style={toggleStyle(sortMode === opt.mode)}
			>
				{#if opt.mode === 'shuffle'}
					<span class="inline-flex items-center gap-1"><Shuffle size={13} aria-hidden="true" />{opt.label}</span>
				{:else}
					{opt.label}
				{/if}
			</button>
		{/each}
		<div class="ml-auto flex items-center gap-1.5">
			<button
				type="button"
				onclick={() => (showUrls = !showUrls)}
				class="{toggleClass} inline-flex items-center gap-1"
				style={toggleStyle(showUrls)}
			>
				<Link2 size={11} aria-hidden="true" /> Show URL
			</button>
			<button
				type="button"
				onclick={() => (showThumbnails = !showThumbnails)}
				class="{toggleClass} inline-flex items-center gap-1"
				style={toggleStyle(showThumbnails)}
			>
				<Image size={11} aria-hidden="true" /> Thumbnail
			</button>
		</div>
	</div>

	<div class="overflow-x-auto">
		<table class="w-full border-collapse text-sm">
			<thead>
				<tr class="text-left" style="color: var(--color-muted)">
					{#if !readonly}<th class="w-6"></th>{/if}
					<th class="py-2 font-medium">{showUrls ? 'URL' : 'Title'}</th>
					<th class="py-2 font-medium">Added</th>
					{#if !readonly}<th class="w-8"></th><th class="w-10"></th>{/if}
				</tr>
			</thead>
			{#if !readonly && sortMode === 'manual'}
				<tbody
					use:dndzone={{ items, flipDurationMs: FLIP, dropTargetStyle: {} }}
					onconsider={onConsider}
					onfinalize={onFinalize}
				>
					{#each items as item (item.id)}
						{@render row(item, true)}
					{/each}
				</tbody>
			{:else}
				<tbody>
					{#each displayItems as item (item.id)}
						{@render row(item, false)}
					{/each}
				</tbody>
			{/if}
		</table>
	</div>
{/if}
