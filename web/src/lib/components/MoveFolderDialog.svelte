<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import type { Folder } from '$lib/types';

	let {
		folderId,
		currentParentId
	}: { folderId: string; currentParentId: string | null } = $props();

	let open = $state(false);
	let folders = $state<Folder[]>([]);
	let loading = $state(false);
	let saving = $state(false);
	let error = $state<string | null>(null);

	// Valid destinations exclude the folder itself and its descendants (would form a cycle).
	const destinations = $derived(validDestinations(folders, folderId));

	function validDestinations(all: Folder[], self: string): Folder[] {
		const childrenOf = new Map<string | null, Folder[]>();
		for (const f of all) (childrenOf.get(f.parentId) ?? childrenOf.set(f.parentId, []).get(f.parentId)!).push(f);
		const blocked = new Set<string>([self]);
		const stack = [self];
		while (stack.length) {
			const id = stack.pop()!;
			for (const c of childrenOf.get(id) ?? []) {
				if (!blocked.has(c.id)) {
					blocked.add(c.id);
					stack.push(c.id);
				}
			}
		}
		return all.filter((f) => !blocked.has(f.id)).sort((a, b) => a.name.localeCompare(b.name));
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

	async function moveTo(parentId: string | null) {
		if (saving || parentId === currentParentId) {
			open = false;
			return;
		}
		saving = true;
		error = null;
		try {
			const res = await api.post(`/folders/${folderId}/move`, { parentId });
			if (res.ok) {
				open = false;
				await invalidateAll();
			} else if (res.status === 400) {
				error = 'That destination is not allowed.';
			} else {
				error = 'Could not move the folder.';
			}
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger class="rounded-md border px-3 py-1.5 text-sm" style="border-color: var(--color-border)">
		Move
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 flex max-h-[80vh] w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="text-lg font-semibold">Move folder</Dialog.Title>
			<Dialog.Description class="mt-1 text-sm" style="color: var(--color-muted)">
				Choose a new location for this folder.
			</Dialog.Description>

			<div class="mt-4 flex-1 overflow-y-auto">
				<button
					type="button"
					onclick={() => moveTo(null)}
					disabled={saving || currentParentId === null}
					class="w-full rounded px-2 py-1.5 text-left text-sm hover:bg-black/5 disabled:opacity-50 dark:hover:bg-white/10"
				>
					Top level (root)
				</button>
				{#if loading}
					<p class="mt-2 text-sm" style="color: var(--color-muted)">Loading…</p>
				{:else}
					<ul class="mt-1 flex flex-col gap-1">
						{#each destinations as f (f.id)}
							<li>
								<button
									type="button"
									onclick={() => moveTo(f.id)}
									disabled={saving || f.id === currentParentId}
									class="w-full truncate rounded px-2 py-1.5 text-left text-sm hover:bg-black/5 disabled:opacity-50 dark:hover:bg-white/10"
								>
									{f.name}
								</button>
							</li>
						{/each}
					</ul>
				{/if}
			</div>

			{#if error}
				<p class="mt-3 text-sm" style="color: var(--color-danger)">{error}</p>
			{/if}

			<div class="mt-4 flex justify-end">
				<Dialog.Close class="rounded-md border px-3 py-2 text-sm" style="border-color: var(--color-border)">Cancel</Dialog.Close>
			</div>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
