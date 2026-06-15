<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import { FolderInput, X } from '@lucide/svelte';
	import FolderPicker from './FolderPicker.svelte';
	import type { Folder } from '$lib/types';

	let {
		folderId,
		currentParentId
	}: { folderId: string; currentParentId: string | null } = $props();

	let open = $state(false);
	let folders = $state<Folder[]>([]);
	let loading = $state(false);
	let busy = $state(false);
	let error = $state<string | null>(null);

	const excludeIds = $derived(computeExcluded(folders, folderId));

	function computeExcluded(all: Folder[], self: string): Set<string> {
		const byParent = new Map<string | null, Folder[]>();
		for (const f of all) {
			if (!byParent.has(f.parentId)) byParent.set(f.parentId, []);
			byParent.get(f.parentId)!.push(f);
		}
		const excluded = new Set<string>([self]);
		const stack = [self];
		while (stack.length) {
			const id = stack.pop()!;
			for (const c of byParent.get(id) ?? []) {
				if (!excluded.has(c.id)) { excluded.add(c.id); stack.push(c.id); }
			}
		}
		return excluded;
	}

	$effect(() => {
		if (open) {
			error = null;
			loading = true;
			api
				.get('/folders')
				.then((res) => (res.ok ? json<Folder[]>(res) : []))
				.then((f) => (folders = f))
				.finally(() => (loading = false));
		}
	});

	async function handleSelect(id: string | null) {
		if (id === currentParentId) { open = false; return; }
		busy = true;
		error = null;
		try {
			const res = await api.post(`/folders/${folderId}/move`, { parentId: id });
			if (res.ok) {
				open = false;
				await invalidateAll();
			} else if (res.status === 400) {
				error = 'That destination is not allowed.';
			} else {
				error = 'Could not move the folder.';
			}
		} finally {
			busy = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger
		class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
		title="Move folder"
		aria-label="Move folder"
	>
		<FolderInput size={15} aria-hidden="true" />
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 flex max-h-[80vh] w-[90vw] max-w-sm -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-center justify-between">
				<Dialog.Title class="font-semibold">Move folder</Dialog.Title>
				<Dialog.Close
					class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					title="Cancel"
					aria-label="Cancel"
				>
					<X size={15} aria-hidden="true" />
				</Dialog.Close>
			</div>

			<div class="mt-4 flex-1 overflow-y-auto">
				{#if loading}
					<p class="text-sm" style="color: var(--color-muted)">Loading…</p>
				{:else}
					<FolderPicker
						{folders}
						selectedId={currentParentId}
						{excludeIds}
						onSelect={handleSelect}
						{busy}
						rootLabel="Top level"
					/>
				{/if}
			</div>

			{#if error}
				<p class="mt-3 text-sm" style="color: var(--color-danger)">{error}</p>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
