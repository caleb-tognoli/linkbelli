<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { enhance } from '$app/forms';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { Plus, X, Check } from '@lucide/svelte';

	type CreateForm = {
		error?: string;
		name?: string;
		description?: string;
		visibility?: string;
		tags?: string;
	} | null;

	let { form = null, folderId = undefined }: { form?: CreateForm; folderId?: string } = $props();

	let open = $state(false);
	let submitting = $state(false);
	let inlineError = $state<string | null>(null);

	// If the create action returned an error, keep/reopen the dialog so it's visible.
	$effect(() => {
		if (form?.error) open = true;
	});

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	async function handleInlineCreate(e: SubmitEvent) {
		e.preventDefault();
		const fd = new FormData(e.target as HTMLFormElement);
		const tagsRaw = (fd.get('tags') as string | null) ?? '';
		submitting = true;
		inlineError = null;
		try {
			const res = await api.post('/playlists', {
				name: fd.get('name'),
				description: fd.get('description') || null,
				visibility: fd.get('visibility') ?? 'Private',
				tags: tagsRaw ? tagsRaw.split(',').map((t) => t.trim()).filter(Boolean) : null
			});
			if (!res.ok) { inlineError = 'Could not create playlist.'; return; }
			const playlist = (await res.json()) as { id: string };
			await api.post(`/folders/${folderId}/playlists`, { playlistId: playlist.id });
			open = false;
			await goto(`/playlists/${playlist.id}`);
		} finally {
			submitting = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Trigger
		class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
		title="New playlist"
		aria-label="New playlist"
	>
		<Plus size={18} aria-hidden="true" />
	</Dialog.Trigger>

	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border p-6 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="text-lg font-semibold">New playlist</Dialog.Title>

			{#if folderId}
				<form class="mt-4 flex flex-col gap-3" onsubmit={handleInlineCreate}>
					<label class="flex flex-col gap-1 text-sm">
						<span>Name</span>
						<input name="name" required class={fieldClass} style={fieldStyle} />
					</label>

					<label class="flex flex-col gap-1 text-sm">
						<span>Description <span style="color: var(--color-muted)">(optional)</span></span>
						<input name="description" class={fieldClass} style={fieldStyle} />
					</label>

					<label class="flex flex-col gap-1 text-sm">
						<span>Visibility</span>
						<select name="visibility" class={fieldClass} style={fieldStyle}>
							<option value="Private">Private</option>
							<option value="Unlisted">Unlisted</option>
							<option value="Public">Public</option>
						</select>
					</label>

					<label class="flex flex-col gap-1 text-sm">
						<span>Tags <span style="color: var(--color-muted)">(comma-separated)</span></span>
						<input name="tags" placeholder="tech, ai" class={fieldClass} style={fieldStyle} />
					</label>

					{#if inlineError}
						<p class="text-sm" style="color: var(--color-danger)">{inlineError}</p>
					{/if}

					<div class="mt-2 flex justify-center gap-2 text-sm">
						<Dialog.Close
							class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							style="border-color: var(--color-border)"
						>
							<X size={15} aria-hidden="true" /> Cancel
						</Dialog.Close>
						<button
							type="submit"
							disabled={submitting}
							class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 font-medium disabled:opacity-60"
							style="background: var(--color-accent); color: var(--color-accent-contrast)"
						>
							<Check size={15} aria-hidden="true" /> {submitting ? 'Creating…' : 'Create'}
						</button>
					</div>
				</form>
			{:else}
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

					<div class="mt-2 flex justify-center gap-2 text-sm">
						<Dialog.Close
							class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							style="border-color: var(--color-border)"
						>
							<X size={15} aria-hidden="true" /> Cancel
						</Dialog.Close>
						<button
							type="submit"
							disabled={submitting}
							class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 font-medium disabled:opacity-60"
							style="background: var(--color-accent); color: var(--color-accent-contrast)"
						>
							<Check size={15} aria-hidden="true" /> {submitting ? 'Creating…' : 'Create'}
						</button>
					</div>
				</form>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
