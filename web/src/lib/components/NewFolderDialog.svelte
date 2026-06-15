<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';

	let {
		parentId = null,
		label = 'New folder',
		triggerClass = 'rounded-md px-3 py-2 text-sm font-medium'
	}: { parentId?: string | null; label?: string; triggerClass?: string } = $props();

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
	<Dialog.Trigger class={triggerClass} style="background: var(--color-accent); color: var(--color-accent-contrast)">
		{label}
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
					<Dialog.Close class="rounded-md border px-3 py-2 text-sm" style="border-color: var(--color-border)">
						Cancel
					</Dialog.Close>
					<button
						type="submit"
						disabled={submitting}
						class="rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
						style="background: var(--color-accent); color: var(--color-accent-contrast)"
					>
						{submitting ? 'Creating…' : 'Create'}
					</button>
				</div>
			</form>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
