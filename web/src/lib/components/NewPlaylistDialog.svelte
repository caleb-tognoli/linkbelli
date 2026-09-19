<script lang="ts">
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button, { buttonClass } from '$lib/components/ui/Button.svelte';
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
					<Field label="Name">
						{#snippet children(f)}
							<Input
								id={f.id}
								name="name"
								required
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>

					<Field label="Description" optional>
						{#snippet children(f)}
							<Input
								id={f.id}
								name="description"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>

					<Field label="Visibility">
						{#snippet children(f)}
							<Select
								id={f.id}
								name="visibility"
								aria-describedby={f.describedby}
							>
							<option value="Private">Private</option>
							<option value="Unlisted">Unlisted</option>
							<option value="Public">Public</option>
							</Select>
						{/snippet}
					</Field>

					<Field label="Tags" hint="comma-separated">
						{#snippet children(f)}
							<Input
								id={f.id}
								name="tags"
								placeholder="tech, ai"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>

					{#if inlineError}
						<p class="text-sm" style="color: var(--color-danger)">{inlineError}</p>
					{/if}

					<div class="mt-2 flex justify-center gap-2 text-sm">
						<Dialog.Close class={buttonClass('secondary')}>
							<X size={17} aria-hidden="true" /> Cancel
						</Dialog.Close>
						<Button type="submit" variant="primary" icon={Check} loading={submitting}>
							{submitting ? 'Creating…' : 'Create'}
						</Button>
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
					<Field label="Name">
						{#snippet children(f)}
							<Input
								id={f.id}
								name="name"
								required
								value={form?.name ?? ''}
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>

					<Field label="Description" optional>
						{#snippet children(f)}
							<Input
								id={f.id}
								name="description"
								value={form?.description ?? ''}
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>

					<Field label="Visibility">
						{#snippet children(f)}
							<Select
								id={f.id}
								name="visibility"
								value={form?.visibility ?? 'Private'}
								aria-describedby={f.describedby}
							>
							<option value="Private">Private</option>
							<option value="Unlisted">Unlisted</option>
							<option value="Public">Public</option>
							</Select>
						{/snippet}
					</Field>

					<Field label="Tags" hint="comma-separated">
						{#snippet children(f)}
							<Input
								id={f.id}
								name="tags"
								value={form?.tags ?? ''}
								placeholder="tech, ai"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>

					{#if form?.error}
						<p class="text-sm" style="color: var(--color-danger)">{form.error}</p>
					{/if}

					<div class="mt-2 flex justify-center gap-2 text-sm">
						<Dialog.Close class={buttonClass('secondary')}>
							<X size={17} aria-hidden="true" /> Cancel
						</Dialog.Close>
						<Button type="submit" variant="primary" icon={Check} loading={submitting}>
							{submitting ? 'Creating…' : 'Create'}
						</Button>
					</div>
				</form>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
