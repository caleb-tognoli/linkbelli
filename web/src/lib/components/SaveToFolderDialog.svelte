<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import { Folder, X } from '@lucide/svelte';
	import FolderPicker from './FolderPicker.svelte';
	import type { Folder as FolderType } from '$lib/types';

	let {
		playlistId,
		currentFolderId = null,
		currentFolderName = null
	}: {
		playlistId: string;
		currentFolderId?: string | null;
		currentFolderName?: string | null;
	} = $props();

	const filed = $derived(currentFolderId !== null);

	let open = $state(false);
	let folders = $state<FolderType[]>([]);
	let loading = $state(false);
	let busy = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			error = null;
			loading = true;
			api
				.get('/folders')
				.then((res) => (res.ok ? json<FolderType[]>(res) : []))
				.then((f) => (folders = f))
				.finally(() => (loading = false));
		}
	});

	async function handleSelect(id: string | null) {
		if (id === null) {
			if (!currentFolderId) { open = false; return; }
			await removeFromFolder();
			return;
		}
		if (id === currentFolderId) { open = false; return; }
		busy = true;
		error = null;
		try {
			const res = await api.post(`/folders/${id}/playlists`, { playlistId });
			if (res.ok || res.status === 204) {
				open = false;
				await invalidateAll();
			} else if (res.status === 404) {
				error = 'This playlist can no longer be saved.';
			} else {
				error = 'Could not save to that folder.';
			}
		} finally {
			busy = false;
		}
	}

	async function removeFromFolder() {
		if (!currentFolderId) return;
		busy = true;
		error = null;
		try {
			const res = await api.del(`/folders/${currentFolderId}/playlists/${playlistId}`);
			if (res.ok || res.status === 204) {
				open = false;
				await invalidateAll();
			} else {
				error = 'Could not remove from the folder.';
			}
		} finally {
			busy = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger
		class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:border-[var(--color-accent)]"
		style="border-color: var(--color-border)"
		title={filed ? `In folder: ${currentFolderName}` : 'Not in a folder'}
	>
		<Folder size={15} aria-hidden="true" style="color: var(--color-muted)" />
		{#if filed}
			<span class="max-w-[12rem] truncate">{currentFolderName}</span>
		{:else}
			<span>Add to folder</span>
		{/if}
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 flex max-h-[80vh] w-[90vw] max-w-sm -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-center justify-between">
				<Dialog.Title class="font-semibold">{filed ? 'Move to folder' : 'Save to folder'}</Dialog.Title>
				<Dialog.Close
					class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					title="Close"
					aria-label="Close"
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
						selectedId={currentFolderId}
						onSelect={handleSelect}
						{busy}
						rootLabel="No folder"
					/>
				{/if}
			</div>

			{#if error}
				<p class="mt-3 text-sm" style="color: var(--color-danger)">{error}</p>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
