<script lang="ts">
	import { Check, X } from '@lucide/svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Modal from '$lib/components/ui/Modal.svelte';
	import { getDialogState, resolveDialog } from '$lib/dialog.svelte';

	/**
	 * The app's confirm and prompt, drawn in the shared Modal.
	 *
	 * The question used to be a paragraph inside an unnamed dialog, so a screen reader opened it
	 * announcing nothing but "dialog", and a prompt's field had no name at all. The question is
	 * the dialog's title now, and a prompt's field is named by it.
	 */
	const dlg = $derived(getDialogState());
	const open = $derived(dlg !== null);

	let promptValue = $state('');
	let field: HTMLInputElement | undefined = $state();

	$effect(() => {
		if (dlg?.kind === 'prompt') {
			promptValue = dlg.defaultValue;
			// Selected, so typing replaces what was suggested and an edit starts at once.
			queueMicrotask(() => field?.select());
		}
	});

	function cancel() {
		resolveDialog(dlg?.kind === 'prompt' ? null : false);
	}

	function confirm() {
		if (dlg?.kind === 'prompt') resolveDialog(promptValue.trim());
		else resolveDialog(true);
	}

	function onOpenChange(next: boolean) {
		if (!next) cancel();
	}
</script>

<Modal
	{open}
	{onOpenChange}
	title={dlg?.message ?? ''}
	description={dlg?.kind === 'confirm' ? dlg.description : undefined}
	size="sm"
>
	{#if dlg?.kind === 'prompt'}
		<Input
			bind:element={field}
			bind:value={promptValue}
			aria-label={dlg.message}
			onkeydown={(e) => e.key === 'Enter' && confirm()}
		/>
	{/if}

	{#snippet footer()}
		<Button icon={X} onclick={cancel}>Cancel</Button>
		<Button
			variant={dlg?.kind === 'confirm' && dlg.danger ? 'danger' : 'primary'}
			icon={Check}
			onclick={confirm}
		>
			{dlg?.confirmLabel ?? (dlg?.kind === 'prompt' ? 'Save' : 'Confirm')}
		</Button>
	{/snippet}
</Modal>
