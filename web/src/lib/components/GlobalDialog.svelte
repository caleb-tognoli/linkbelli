<script lang="ts">
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
					<input
						type="text"
						bind:value={promptValue}
						class="mt-3 w-full rounded-md border px-3 py-2 text-sm"
						style="border-color: var(--color-border); background: var(--color-bg)"
						onkeydown={(e) => e.key === 'Enter' && confirm()}
					/>
				{/if}

				<div class="mt-4 flex justify-center gap-2 text-sm">
					<button
						type="button"
						onclick={cancel}
						class="rounded-md border px-3 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						Cancel
					</button>
					<button
						type="button"
						onclick={confirm}
						class="rounded-md px-3 py-1.5 font-medium"
						style={dlg.kind === 'confirm' && dlg.danger
							? 'background: var(--color-danger); color: var(--color-accent-contrast)'
							: 'background: var(--color-accent); color: var(--color-accent-contrast)'}
					>
						{dlg.confirmLabel ?? (dlg.kind === 'prompt' ? 'Save' : 'Confirm')}
					</button>
				</div>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
