<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { enhance } from '$app/forms';
	import { Plus, X, Check } from '@lucide/svelte';

	type CreateForm = {
		error?: string;
		name?: string;
		description?: string;
		visibility?: string;
		tags?: string;
	} | null;

	let { form }: { form: CreateForm } = $props();

	let open = $state(false);
	let submitting = $state(false);

	// If the create action returned an error, keep/reopen the dialog so it's visible.
	$effect(() => {
		if (form?.error) open = true;
	});

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger
		class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
		title="New playlist"
		aria-label="New playlist"
	>
		<Plus size={16} aria-hidden="true" />
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="text-lg font-semibold">New playlist</Dialog.Title>
			<Dialog.Description class="mt-1 text-sm" style="color: var(--color-muted)">
				Create a playlist to collect links.
			</Dialog.Description>

			<form
				method="post"
				action="?/create"
				class="mt-4 flex flex-col gap-3"
				use:enhance={() => {
					submitting = true;
					return async ({ update }) => {
						await update();
						submitting = false;
					};
				}}
			>
				<label class="flex flex-col gap-1 text-sm">
					<span>Name</span>
					<input
						name="name"
						required
						value={form?.name ?? ''}
						class={fieldClass}
						style={fieldStyle}
					/>
				</label>

				<label class="flex flex-col gap-1 text-sm">
					<span>Description <span style="color: var(--color-muted)">(optional)</span></span>
					<input name="description" value={form?.description ?? ''} class={fieldClass} style={fieldStyle} />
				</label>

				<label class="flex flex-col gap-1 text-sm">
					<span>Visibility</span>
					<select name="visibility" class={fieldClass} style={fieldStyle} value={form?.visibility ?? 'Private'}>
						<option value="Private">Private</option>
						<option value="Unlisted">Unlisted</option>
						<option value="Public">Public</option>
					</select>
				</label>

				<label class="flex flex-col gap-1 text-sm">
					<span>Tags <span style="color: var(--color-muted)">(comma-separated)</span></span>
					<input name="tags" value={form?.tags ?? ''} placeholder="tech, ai" class={fieldClass} style={fieldStyle} />
				</label>

				{#if form?.error}
					<p class="text-sm" style="color: var(--color-danger)">{form.error}</p>
				{/if}

				<div class="mt-2 flex justify-end gap-2">
					<Dialog.Close
						class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						title="Cancel"
						aria-label="Cancel"
					>
						<X size={15} aria-hidden="true" />
					</Dialog.Close>
					<button
						type="submit"
						disabled={submitting}
						class="inline-flex items-center rounded-md p-2 disabled:opacity-60"
						style="background: var(--color-accent); color: var(--color-accent-contrast)"
						title={submitting ? 'Creating…' : 'Create'}
						aria-label="Create playlist"
					>
						<Check size={15} aria-hidden="true" />
					</button>
				</div>
			</form>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
