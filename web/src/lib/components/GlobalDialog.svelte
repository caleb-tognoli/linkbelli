<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { Check, X } from '@lucide/svelte';
	import { Dialog } from 'bits-ui';
	import { getDialogState, resolveDialog } from '$lib/dialog.svelte';

	const dlg = $derived(getDialogState());
	const open = $derived(dlg !== null);

	let promptValue = $state('');

	$effect(() => {
		if (dlg?.kind === 'prompt') promptValue = dlg.defaultValue;
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

<Dialog.Root {open} {onOpenChange}>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-sm -translate-x-1/2 -translate-y-1/2 rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			{#if dlg}
				<p class="font-medium">{dlg.message}</p>

				{#if dlg.kind === 'prompt'}
					<Input
						type="text"
						bind:value={promptValue}
						onkeydown={(e) => e.key === 'Enter' && confirm()}
						class="mt-3 w-full"
					/>
				{/if}

				<div class="mt-4 flex justify-center gap-2 text-sm">
					<Button icon={X} onclick={cancel}>Cancel</Button>
					<Button
						variant={dlg.kind === 'confirm' && dlg.danger ? 'danger' : 'primary'}
						icon={Check}
						onclick={confirm}
					>
						{dlg.confirmLabel ?? (dlg.kind === 'prompt' ? 'Save' : 'Confirm')}
					</Button>
				</div>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
