<script lang="ts">
	import SkeletonRows from '$lib/components/ui/SkeletonRows.svelte';
	import Modal from '$lib/components/ui/Modal.svelte';
	import { buttonClass } from '$lib/components/ui/Button.svelte';
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import { Folder, FolderInput } from '@lucide/svelte';
	import FolderPicker from './FolderPicker.svelte';
	import type { Folder as FolderType } from '$lib/types';

	let {
		playlistId,
		currentFolderId = null,
		currentFolderName = null,
		compact = false
	}: {
		playlistId: string;
		currentFolderId?: string | null;
		currentFolderName?: string | null;
		compact?: boolean;
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

<Modal bind:open title={filed ? 'Move to another folder' : 'Add to folder'} size="sm">
	{#snippet trigger()}
		<Dialog.Trigger
			class={compact ? buttonClass('ghost', 'sm', true) : buttonClass('secondary', 'sm')}
			title={filed ? `In ${currentFolderName} — move it` : 'Add to folder'}
			aria-label={filed ? `In ${currentFolderName} — move it to another folder` : 'Add to folder'}
		>
			{#if compact}
				<FolderInput size={15} aria-hidden="true" />
			{:else}
				<Folder size={15} aria-hidden="true" />
				{#if filed}
					<span class="sr-only max-w-[12rem] truncate md:not-sr-only">{currentFolderName}</span>
				{:else}
					<span class="sr-only md:not-sr-only">Add to folder</span>
				{/if}
			{/if}
		</Dialog.Trigger>
	{/snippet}

	<div class="flex-1 overflow-y-auto">
		{#if loading}
			<SkeletonRows rows={5} />
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
		<p class="mt-3 text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
	{/if}
</Modal>
