<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { FolderPlus, X, Check } from '@lucide/svelte';

	let {
		parentId = null,
		label = 'New folder',
		triggerClass = 'rounded-md px-3 py-2 text-sm font-medium',
		triggerStyle = 'background: var(--color-accent); color: var(--color-accent-contrast)'
	}: { parentId?: string | null; label?: string; triggerClass?: string; triggerStyle?: string } = $props();

	let open = $state(false);
	let name = $state('');
	let submitting = $state(false);
	let error = $state<string | null>(null);

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	async function create(e: SubmitEvent) {
		e.preventDefault();
		if (!name.trim() || submitting) return;
		submitting = true;
		error = null;
		try {
			const res = await api.post('/folders', { name: name.trim(), parentId });
			if (res.ok || res.status === 201) {
				name = '';
				open = false;
				await invalidateAll();
			} else {
				error = 'Could not create the folder.';
			}
		} finally {
			submitting = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger class={triggerClass} style={triggerStyle} title={label || 'New folder'} aria-label={label || 'New folder'}>
		<span class="inline-flex items-center gap-1.5">
			<FolderPlus size={18} aria-hidden="true" />
			{#if label}{label}{/if}
		</span>
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="text-lg font-semibold">New folder</Dialog.Title>
			<Dialog.Description class="mt-1 text-sm" style="color: var(--color-muted)">
				{parentId ? 'Create a subfolder here.' : 'Create a top-level folder.'}
			</Dialog.Description>

			<form class="mt-4 flex flex-col gap-3" onsubmit={create}>
				<label class="flex flex-col gap-1 text-sm">
					<span>Name</span>
					<!-- svelte-ignore a11y_autofocus -->
					<input bind:value={name} required autofocus class={fieldClass} style={fieldStyle} />
				</label>

				{#if error}
					<p class="text-sm" style="color: var(--color-danger)">{error}</p>
				{/if}

				<div class="mt-2 flex justify-end gap-2">
					<Dialog.Close class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10" title="Cancel" aria-label="Cancel">
						<X size={17} aria-hidden="true" />
					</Dialog.Close>
					<button
						type="submit"
						disabled={submitting}
						class="inline-flex items-center rounded-md p-2 disabled:opacity-60"
						style="background: var(--color-accent); color: var(--color-accent-contrast)"
						title={submitting ? 'Creating…' : 'Create'}
						aria-label="Create folder"
					>
						<Check size={17} aria-hidden="true" />
					</button>
				</div>
			</form>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
