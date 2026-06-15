<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import type { Folder } from '$lib/types';

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
	let folders = $state<Folder[]>([]);
	let loading = $state(false);
	let busy = $state(false);
	let error = $state<string | null>(null);
	let newName = $state('');

	// Render folders as an indented tree reflecting nesting depth.
	type Row = { folder: Folder; depth: number };
	const rows = $derived(buildRows(folders));

	function buildRows(all: Folder[]): Row[] {
		const byParent = new Map<string | null, Folder[]>();
		for (const f of all) (byParent.get(f.parentId) ?? byParent.set(f.parentId, []).get(f.parentId)!).push(f);
		for (const list of byParent.values()) list.sort((a, b) => a.name.localeCompare(b.name));
		const out: Row[] = [];
		const walk = (parentId: string | null, depth: number) => {
			for (const f of byParent.get(parentId) ?? []) {
				out.push({ folder: f, depth });
				walk(f.id, depth + 1);
			}
		};
		walk(null, 0);
		return out;
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

	async function saveTo(folderId: string) {
		if (busy) return;
		if (folderId === currentFolderId) {
			open = false;
			return;
		}
		busy = true;
		error = null;
		try {
			const res = await api.post(`/folders/${folderId}/playlists`, { playlistId });
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
		if (busy || !currentFolderId) return;
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

	async function createAndSave(e: SubmitEvent) {
		e.preventDefault();
		if (!newName.trim() || busy) return;
		busy = true;
		error = null;
		try {
			const res = await api.post('/folders', { name: newName.trim(), parentId: null });
			if (res.ok || res.status === 201) {
				const created = await json<Folder>(res);
				newName = '';
				await saveTo(created.id);
			} else {
				error = 'Could not create the folder.';
			}
		} finally {
			busy = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<!-- The trigger doubles as the current-folder indicator. -->
	<Dialog.Trigger
		class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:border-[var(--color-accent)]"
		style="border-color: var(--color-border)"
		title={filed ? `In folder: ${currentFolderName}` : 'Not in a folder'}
	>
		<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="color: var(--color-muted)">
			<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
		</svg>
		{#if filed}
			<span class="max-w-[12rem] truncate">{currentFolderName}</span>
		{:else}
			<span>Add to folder</span>
		{/if}
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 flex max-h-[80vh] w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="text-lg font-semibold">{filed ? 'Move to folder' : 'Save to folder'}</Dialog.Title>
			<Dialog.Description class="mt-1 text-sm" style="color: var(--color-muted)">
				Folders are private to you. A playlist lives in one folder.
			</Dialog.Description>

			<div class="mt-4 flex-1 overflow-y-auto">
				{#if loading}
					<p class="text-sm" style="color: var(--color-muted)">Loading folders…</p>
				{:else if rows.length === 0}
					<p class="text-sm" style="color: var(--color-muted)">No folders yet. Create one below.</p>
				{:else}
					<ul class="flex flex-col gap-1">
						{#each rows as { folder, depth } (folder.id)}
							{@const current = folder.id === currentFolderId}
							<li>
								<button
									type="button"
									onclick={() => saveTo(folder.id)}
									disabled={busy}
									class="flex w-full items-center justify-between gap-2 rounded px-2 py-1.5 text-left text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
									style={`padding-left: ${0.5 + depth * 1}rem`}
								>
									<span class="truncate">{folder.name}</span>
									{#if current}
										<span class="shrink-0 text-xs" style="color: var(--color-muted)">Current</span>
									{/if}
								</button>
							</li>
						{/each}
					</ul>
				{/if}
			</div>

			<form class="mt-4 flex gap-2 border-t pt-4" style="border-color: var(--color-border)" onsubmit={createAndSave}>
				<input
					bind:value={newName}
					placeholder="New folder name"
					class="flex-1 rounded-md border px-3 py-2 text-sm"
					style="border-color: var(--color-border); background: var(--color-bg)"
				/>
				<button
					type="submit"
					disabled={busy || !newName.trim()}
					class="rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
					style="background: var(--color-accent); color: var(--color-accent-contrast)"
				>
					Create & {filed ? 'move' : 'save'}
				</button>
			</form>

			{#if error}
				<p class="mt-3 text-sm" style="color: var(--color-danger)">{error}</p>
			{/if}

			<div class="mt-4 flex items-center justify-between">
				{#if filed}
					<button
						type="button"
						onclick={removeFromFolder}
						disabled={busy}
						class="rounded-md border px-3 py-2 text-sm disabled:opacity-60"
						style="border-color: var(--color-border); color: var(--color-danger)"
					>
						Remove from folder
					</button>
				{:else}
					<span></span>
				{/if}
				<Dialog.Close class="rounded-md border px-3 py-2 text-sm" style="border-color: var(--color-border)">
					Close
				</Dialog.Close>
			</div>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
