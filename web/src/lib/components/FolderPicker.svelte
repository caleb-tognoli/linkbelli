<script lang="ts">
	import { api } from '$lib/api/client';
	import { Folder as FolderIcon, House, Plus, X } from '@lucide/svelte';
	import type { Folder } from '$lib/types';

	let {
		folders,
		selectedId = undefined,
		excludeIds = new Set<string>(),
		onSelect,
		busy = false,
		rootLabel = 'Root'
	}: {
		folders: Folder[];
		selectedId?: string | null;
		excludeIds?: Set<string>;
		onSelect: (id: string | null) => void;
		busy?: boolean;
		rootLabel?: string;
	} = $props();

	let folderList = $state<Folder[]>([]);
	$effect(() => { folderList = [...folders]; });

	let activeAdd = $state<{ parentId: string | null } | null>(null);
	let newName = $state('');
	let creating = $state(false);

	function startAdd(parentId: string | null) {
		activeAdd = { parentId };
		newName = '';
	}

	function cancelAdd() {
		activeAdd = null;
		newName = '';
	}

	async function doCreate() {
		const name = newName.trim();
		if (!name || creating || !activeAdd) return;
		creating = true;
		try {
			const res = await api.post('/folders', { name, parentId: activeAdd.parentId });
			if (res.ok || res.status === 201) {
				const created = (await res.json()) as Folder;
				folderList = [...folderList, created];
				cancelAdd();
				onSelect(created.id);
			}
		} finally {
			creating = false;
		}
	}

	const childrenOf = $derived(buildMap(folderList, excludeIds));

	function buildMap(all: Folder[], exclude: Set<string>) {
		const map = new Map<string | null, Folder[]>();
		for (const f of all) {
			if (exclude.has(f.id)) continue;
			const key: string | null = f.parentId;
			if (!map.has(key)) map.set(key, []);
			map.get(key)!.push(f);
		}
		for (const arr of map.values()) arr.sort((a, b) => a.name.localeCompare(b.name));
		return map;
	}
</script>

{#snippet addForm(parentId: string | null, depth: number)}
	{#if activeAdd && activeAdd.parentId === parentId}
		<div class="flex items-center gap-1 py-0.5" style="padding-left: {depth * 1.25 + 0.25}rem">
			<input
				bind:value={newName}
				placeholder="Folder name…"
				disabled={creating}
				class="flex-1 rounded border px-2 py-0.5 text-sm"
				style="border-color: var(--color-border); background: var(--color-bg)"
				onkeydown={(e) => { if (e.key === 'Enter') doCreate(); if (e.key === 'Escape') cancelAdd(); }}
				autofocus
			/>
			<button
				type="button"
				onclick={cancelAdd}
				class="inline-flex items-center rounded p-0.5 hover:bg-black/5 dark:hover:bg-white/10"
				style="color: var(--color-muted)"
				title="Cancel"
				aria-label="Cancel"
			>
				<X size={12} aria-hidden="true" />
			</button>
		</div>
	{/if}
{/snippet}

{#snippet treeItems(parentId: string | null, depth: number)}
	{#each childrenOf.get(parentId) ?? [] as f (f.id)}
		<li>
			<div class="flex items-center gap-0.5" style="padding-left: {depth * 1.25}rem">
				<button
					type="button"
					onclick={() => onSelect(f.id)}
					disabled={busy || creating}
					class="flex flex-1 items-center gap-1.5 truncate rounded px-1.5 py-1 text-left text-sm hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60"
					style={selectedId === f.id ? 'color: var(--color-accent)' : ''}
					title={f.name}
				>
					<FolderIcon size={13} aria-hidden="true" class="shrink-0" style="color: var(--color-muted)" />
					<span class="truncate">{f.name}</span>
				</button>
				<button
					type="button"
					onclick={() => startAdd(f.id)}
					disabled={creating}
					class="inline-flex shrink-0 items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-40"
					style="color: var(--color-muted)"
					title="New subfolder in {f.name}"
					aria-label="New subfolder in {f.name}"
				>
					<Plus size={11} aria-hidden="true" />
				</button>
			</div>
			{@render addForm(f.id, depth + 1)}
			{@render treeItems(f.id, depth + 1)}
		</li>
	{/each}
{/snippet}

<ul class="select-none text-sm">
	<li>
		<div class="flex items-center gap-0.5">
			<button
				type="button"
				onclick={() => onSelect(null)}
				disabled={busy || creating}
				class="flex flex-1 items-center gap-1.5 rounded px-1.5 py-1 text-left hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60"
				style={selectedId === null ? 'color: var(--color-accent)' : 'color: var(--color-muted)'}
			>
				<House size={13} aria-hidden="true" class="shrink-0" />
				<span class="font-medium">{rootLabel}</span>
			</button>
			<button
				type="button"
				onclick={() => startAdd(null)}
				disabled={creating}
				class="inline-flex shrink-0 items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-40"
				style="color: var(--color-muted)"
				title="New top-level folder"
				aria-label="New top-level folder"
			>
				<Plus size={11} aria-hidden="true" />
			</button>
		</div>
		{@render addForm(null, 1)}
		{@render treeItems(null, 1)}
	</li>
</ul>
