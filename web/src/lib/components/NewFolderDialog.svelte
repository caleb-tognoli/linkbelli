<script lang="ts">
	import Modal, { MODAL_FOOTER } from '$lib/components/ui/Modal.svelte';
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

<Modal bind:open title="New folder">
	{#snippet trigger()}
		<Dialog.Trigger
			class={buttonClass(variant, 'md', iconOnly)}
			title={iconOnly ? 'New folder' : undefined}
			aria-label={iconOnly ? 'New folder' : undefined}
		>
			<FolderPlus size={17} aria-hidden="true" />
			{#if !iconOnly}New folder{/if}
		</Dialog.Trigger>
	{/snippet}

	<form class="flex flex-col gap-3" onsubmit={create}>
		<Field label="Name" required>
			{#snippet children(f)}
				<!-- svelte-ignore a11y_autofocus -- a dialog whose only field is this one -->
				<Input id={f.id} bind:value={name} required autofocus aria-describedby={f.describedby} />
			{/snippet}
		</Field>

		{#if error}
			<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
		{/if}

		<div class={MODAL_FOOTER}>
			<Dialog.Close class={buttonClass('secondary')}>
				<X size={17} aria-hidden="true" /> Cancel
			</Dialog.Close>
			<Button type="submit" variant="primary" icon={Check} loading={submitting}>
				{submitting ? 'Creating…' : 'Create folder'}
			</Button>
		</div>
	</form>
</Modal>
