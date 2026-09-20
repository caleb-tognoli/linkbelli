<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button, { buttonClass, iconSize } from '$lib/components/ui/Button.svelte';
	import Modal, { MODAL_FOOTER } from '$lib/components/ui/Modal.svelte';
	import { Dialog } from 'bits-ui';
	import { enhance } from '$app/forms';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { Plus, X, Check } from '@lucide/svelte';
	import Textarea from '$lib/components/ui/Textarea.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import { toast } from '$lib/toast.svelte';
	import { failureMessage } from '$lib/api/errors';
	import type { TagSummary, Visibility } from '$lib/types';

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
	let name = $state(form?.name ?? '');
	/**
	 * What is wrong with the name, once it has been asked for.
	 *
	 * The form used to lean on the browser's own `required`, which pops an OS-styled bubble over
	 * the dialog — light grey on a dark page, gone again on its own, and nothing to do with the
	 * error channel every other form here uses. Field already has one, and it announces itself.
	 */
	let nameError = $state<string | null>(null);

	/** True when the form may go. Fills in the error and moves the keyboard there when not. */
	function nameIsGiven(formEl: HTMLFormElement): boolean {
		if (name.trim()) {
			nameError = null;
			return true;
		}
		nameError = 'Give the playlist a name.';
		formEl.querySelector<HTMLInputElement>('input[name="name"]')?.focus();
		return false;
	}

	// Held here rather than read off the form, because the choice is a radio group now and its
	// value has to reach the server action in a field of its own.
	let visibility = $state<Visibility>((form?.visibility as Visibility) ?? 'Private');
	let tags = $state(form?.tags ?? '');

	const VISIBILITIES: { value: Visibility; label: string; description: string }[] = [
		{ value: 'Private', label: 'Private', description: 'Only you.' },
		{ value: 'Unlisted', label: 'Unlisted', description: 'Anyone with the link, and nobody else.' },
		{ value: 'Public', label: 'Public', description: 'Listed on Discover and on your profile.' }
	];
	const visibilityNote = $derived(VISIBILITIES.find((v) => v.value === visibility)?.description ?? '');

	/**
	 * The tags already in use, offered rather than remembered.
	 *
	 * Typing them by hand is how "ai", "AI" and "a.i." end up as three tags with one playlist
	 * each. Fetched when the dialog opens, because most of the time it never does.
	 */
	let known = $state<TagSummary[]>([]);
	$effect(() => {
		if (!open || known.length > 0) return;
		void api
			.get('/tags')
			.then((res) => (res.ok ? (res.json() as Promise<TagSummary[]>) : []))
			.then((list) => (known = list.slice(0, 8)))
			.catch(() => {
				// Typing them still works.
			});
	});

	const chosen = $derived(
		tags.split(',').map((t) => t.trim().toLowerCase()).filter(Boolean)
	);

	function toggleTag(name: string) {
		tags = (chosen.includes(name.toLowerCase())
			? chosen.filter((t) => t !== name.toLowerCase())
			: [...chosen, name.toLowerCase()]
		).join(', ');
	}

	// If the create action returned an error, keep/reopen the dialog so it's visible.
	$effect(() => {
		if (form?.error) open = true;
	});


	async function handleInlineCreate(e: SubmitEvent) {
		e.preventDefault();
		if (!nameIsGiven(e.target as HTMLFormElement)) return;
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
			if (!res.ok) {
				inlineError = failureMessage(res, 'Could not create playlist.');
				return;
			}
			const playlist = (await res.json()) as { id: string };
			// Made, but not filed: said out loud rather than left for somebody to notice that the
			// folder they were standing in is still empty.
			const filed = await api.post(`/folders/${folderId}/playlists`, { playlistId: playlist.id });
			open = false;
			if (!filed.ok) toast.error('Playlist created, but it could not be added to this folder.');
			await goto(`/playlists/${playlist.id}`);
		} finally {
			submitting = false;
		}
	}
</script>

{#snippet fields(values: CreateForm)}
	<Field label="Name" required error={nameError}>
		{#snippet children(f)}
			<!-- svelte-ignore a11y_autofocus -- a dialog opened to be typed into -->
			<Input
				id={f.id}
				name="name"
				autofocus
				bind:value={name}
				invalid={f.invalid}
				aria-required="true"
				aria-describedby={f.describedby}
			/>
		{/snippet}
	</Field>

	<Field label="Description" optional>
		{#snippet children(f)}
			<!-- Two rows: a description is a sentence or two about what the list is for, and a
			     single-line box said it was meant to be three words. -->
			<Textarea
				id={f.id}
				name="description"
				rows={2}
				value={values?.description ?? ''}
				aria-describedby={f.describedby}
			/>
		{/snippet}
	</Field>

	<div class="flex flex-col gap-1 text-sm">
		<!-- Named and explained, rather than three words in a select: "Unlisted" is not something
		     anybody can be expected to know the rules of. -->
		<SegmentedControl
			label="Visibility"
			options={VISIBILITIES.map((v) => ({ value: v.value, label: v.label }))}
			bind:value={visibility}
		/>
		<p class="text-xs text-muted">{visibilityNote}</p>
		<input type="hidden" name="visibility" value={visibility} />
	</div>

	<Field label="Tags" hint="Comma-separated">
		{#snippet children(f)}
			<Input
				id={f.id}
				name="tags"
				bind:value={tags}
				placeholder="tech, ai"
				aria-describedby={f.describedby}
			/>
		{/snippet}
	</Field>

	{#if known.length > 0}
		<div class="-mt-1 flex flex-wrap items-center gap-1">
			<span class="text-xs text-muted">Yours:</span>
			{#each known as tag (tag.name)}
				{@const on = chosen.includes(tag.name.toLowerCase())}
				<button
					type="button"
					onclick={() => toggleTag(tag.name)}
					aria-pressed={on}
					class="inline-flex min-h-6 max-w-full items-center rounded-full px-2 text-xs {on
						? 'border border-accent bg-selected text-accent'
						: 'bg-chip text-text hover:brightness-95'}"
				>
					{tag.name}
				</button>
			{/each}
		</div>
	{/if}
{/snippet}

{#snippet actions()}
	<div class={MODAL_FOOTER}>
		<Dialog.Close class={buttonClass('secondary')}>
			<X size={17} aria-hidden="true" /> Cancel
		</Dialog.Close>
		<Button type="submit" variant="primary" icon={Check} loading={submitting}>
			{submitting ? 'Creating…' : 'Create'}
		</Button>
	</div>
{/snippet}

<Modal bind:open title="New playlist">
	{#snippet trigger()}
		<!-- The page's main action, so it says what it does: it was a bare plus among the utility
		     icons, the same size and weight as Trash. -->
		<Dialog.Trigger class={buttonClass('primary')}>
			<Plus size={iconSize()} aria-hidden="true" />
			New playlist
		</Dialog.Trigger>
	{/snippet}

	{#if folderId}
		<form class="flex flex-col gap-3" novalidate onsubmit={handleInlineCreate}>
			{@render fields(null)}

			{#if inlineError}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{inlineError}</p>
			{/if}

			{@render actions()}
		</form>
	{:else}
		<form
			method="post"
			action="?/create"
			class="flex flex-col gap-3"
			novalidate
			use:enhance={({ cancel, formElement }) => {
				if (!nameIsGiven(formElement)) {
					cancel();
					return;
				}
				submitting = true;
				return async ({ update }) => {
					await update();
					submitting = false;
				};
			}}
		>
			{@render fields(form)}

			{#if form?.error}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
			{/if}

			{@render actions()}
		</form>
	{/if}
</Modal>
