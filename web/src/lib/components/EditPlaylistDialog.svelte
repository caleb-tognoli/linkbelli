<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { Check, Pencil, X } from '@lucide/svelte';
	import Button, { buttonClass } from '$lib/components/ui/Button.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Textarea from '$lib/components/ui/Textarea.svelte';
	import Modal, { MODAL_FOOTER } from '$lib/components/ui/Modal.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import { api } from '$lib/api/client';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import { confirmDialog } from '$lib/dialog.svelte';
	import type { NsfwSetting, Playlist, Visibility } from '$lib/types';

	/**
	 * What a playlist says about itself, all in one place.
	 *
	 * The name could be renamed in the header and the visibility changed from a menu, but the
	 * description was set once at creation and was read-only ever after, and the adult-content
	 * setting only appeared for a playlist already flagged — so a wrongly *un*flagged one could
	 * not be marked at all.
	 */
	let {
		playlist,
		name = $bindable(),
		description = $bindable(),
		visibility = $bindable(),
		nsfwSetting = $bindable(),
		onsaved
	}: {
		playlist: Playlist;
		name: string;
		description: string | null;
		visibility: Visibility;
		nsfwSetting: NsfwSetting;
		/** The saved playlist, for whatever the page shows that this dialog does not own. */
		onsaved?: (playlist: Playlist) => void;
	} = $props();

	let open = $state(false);
	let saving = $state(false);
	let error = $state<string | null>(null);

	let draftName = $state(name);
	let draftDescription = $state(description ?? '');
	let draftVisibility = $state<Visibility>(visibility);
	let draftNsfw = $state<NsfwSetting>(nsfwSetting);

	// Opening picks up whatever the page shows now, so a rename made in the header is not undone
	// by a dialog still holding the old one.
	function onOpenChange(next: boolean) {
		if (!next) return;
		draftName = name;
		draftDescription = description ?? '';
		draftVisibility = visibility;
		draftNsfw = nsfwSetting;
		error = null;
	}

	const VISIBILITIES: { value: Visibility; label: string; description: string }[] = [
		{ value: 'Private', label: 'Private', description: 'Only you.' },
		{ value: 'Unlisted', label: 'Unlisted', description: 'Anyone with the link, and nobody else.' },
		{ value: 'Public', label: 'Public', description: 'Listed on Discover and on your profile.' }
	];
	const visibilityNote = $derived(
		VISIBILITIES.find((v) => v.value === draftVisibility)?.description ?? ''
	);

	async function save(event: SubmitEvent) {
		event.preventDefault();
		if (!draftName.trim()) {
			error = 'Give this playlist a name.';
			return;
		}

		// The same question the visibility menu asks: publishing is the one change here that other
		// people see, and it cannot be taken back from anyone who has already looked.
		if (draftVisibility === 'Public' && visibility !== 'Public') {
			const ok = await confirmDialog(`Make "${name}" public?`, {
				description:
					'Anyone can find it on Discover and on your profile, and read every link and note in it.',
				confirmLabel: 'Make public'
			});
			if (!ok) return;
		}

		saving = true;
		error = null;
		const res = await api.patch(`/playlists/${playlist.id}`, {
			name: draftName.trim(),
			description: draftDescription.trim() || null,
			visibility: draftVisibility,
			nsfw: draftNsfw
		});
		saving = false;

		if (!res.ok) {
			error = failureMessage(res, 'Could not save those details.');
			return;
		}

		const saved = (await res.json()) as Playlist;
		name = saved.name;
		description = saved.description ?? null;
		visibility = saved.visibility;
		nsfwSetting = saved.nsfwSetting ?? 'Auto';
		onsaved?.(saved);
		open = false;
		toast.success('Saved.');
	}
</script>

<Modal bind:open {onOpenChange} title="Edit details">
	{#snippet trigger()}
		<Dialog.Trigger class={buttonClass('secondary', 'sm')} title="Edit this playlist's details">
			<Pencil size={15} aria-hidden="true" />
			<span class="sr-only md:not-sr-only">Edit details</span>
		</Dialog.Trigger>
	{/snippet}

	<form class="flex flex-col gap-3" onsubmit={save} novalidate>
		<Field label="Name" required error={error && !draftName.trim() ? error : null}>
			{#snippet children(f)}
				<Input id={f.id} bind:value={draftName} required invalid={f.invalid} aria-describedby={f.describedby} />
			{/snippet}
		</Field>

		<Field label="Description" optional>
			{#snippet children(f)}
				<Textarea id={f.id} rows={3} bind:value={draftDescription} aria-describedby={f.describedby} />
			{/snippet}
		</Field>

		<div class="flex flex-col gap-1 text-sm">
			<SegmentedControl
				label="Visibility"
				options={VISIBILITIES.map((v) => ({ value: v.value, label: v.label }))}
				bind:value={draftVisibility}
			/>
			<p class="text-xs text-muted">{visibilityNote}</p>
		</div>

		<div class="flex flex-col gap-1 text-sm">
			<SegmentedControl
				label="Adult content"
				options={[
					{ value: 'Auto', label: 'Automatic' },
					{ value: 'No', label: 'Not adult' },
					{ value: 'Yes', label: 'Adult' }
				]}
				bind:value={draftNsfw}
			/>
			<p class="text-xs text-muted">
				{draftNsfw === 'Auto'
					? 'Goes by what the sites themselves declare.'
					: draftNsfw === 'Yes'
						? 'Hidden from anyone who has not asked to see adult content.'
						: 'Shown to everyone, whatever the sites declare.'}
			</p>
		</div>

		{#if error && draftName.trim()}
			<p class="text-sm text-danger" role="alert">{error}</p>
		{/if}

		<div class={MODAL_FOOTER}>
			<Dialog.Close class={buttonClass('secondary')}>
				<X size={17} aria-hidden="true" /> Cancel
			</Dialog.Close>
			<Button type="submit" variant="primary" icon={Check} loading={saving}>
				{saving ? 'Saving…' : 'Save'}
			</Button>
		</div>
	</form>
</Modal>
