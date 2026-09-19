<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button, { buttonClass, type ButtonVariant } from '$lib/components/ui/Button.svelte';
	import { Dialog } from 'bits-ui';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { FolderPlus, X, Check } from '@lucide/svelte';

	let {
		parentId = null,
		variant = 'secondary',
		iconOnly = false
	}: { parentId?: string | null; variant?: ButtonVariant; iconOnly?: boolean } = $props();

	let open = $state(false);
	let name = $state('');
	let submitting = $state(false);
	let error = $state<string | null>(null);


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
	<Dialog.Trigger
		class={buttonClass(variant, 'md', iconOnly)}
		title={iconOnly ? 'New folder' : undefined}
		aria-label={iconOnly ? 'New folder' : undefined}
	>
		<FolderPlus size={17} aria-hidden="true" />
		{#if !iconOnly}New folder{/if}
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="text-lg font-semibold">New folder</Dialog.Title>

			<form class="mt-4 flex flex-col gap-3" onsubmit={create}>
				<Field label="Name">
					{#snippet children(f)}
						<!-- svelte-ignore a11y_autofocus -- a dialog whose only field is this one -->
						<Input id={f.id} bind:value={name} required autofocus aria-describedby={f.describedby} />
					{/snippet}
				</Field>

				{#if error}
					<p class="text-sm" style="color: var(--color-danger)">{error}</p>
				{/if}

				<div class="mt-2 flex justify-center gap-2 text-sm">
					<Dialog.Close class={buttonClass('secondary')}>
						<X size={17} aria-hidden="true" /> Cancel
					</Dialog.Close>
					<Button type="submit" variant="primary" icon={Check} loading={submitting}>
						{submitting ? 'Creating…' : 'Create folder'}
					</Button>
				</div>
			</form>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
