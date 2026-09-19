<script lang="ts">
	import Modal from '$lib/components/ui/Modal.svelte';
	import Textarea from '$lib/components/ui/Textarea.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button, { buttonClass } from '$lib/components/ui/Button.svelte';
	import { Dialog } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { Flag } from '@lucide/svelte';

	let { username, slug }: { username: string; slug: string } = $props();

	const reasons = [
		{ value: 'Spam', label: 'Spam' },
		{ value: 'Nsfw', label: 'Adult content, unflagged' },
		{ value: 'Malware', label: 'Malware' },
		{ value: 'Copyright', label: 'Published without its author' },
		{ value: 'Illegal', label: 'Unlawful' },
		{ value: 'Other', label: 'Something else' }
	];

	let open = $state(false);
	let reason = $state('Spam');
	let note = $state('');
	let busy = $state(false);
	let sent = $state(false);
	let error = $state<string | null>(null);

	async function send() {
		busy = true;
		error = null;
		try {
			const res = await api.post(
				`/public/playlists/${encodeURIComponent(username)}/${encodeURIComponent(slug)}/report`,
				{ reason, note: note.trim() || null }
			);

			if (res.ok) {
				sent = true;
				return;
			}

			error =
				res.status === 401
					? 'Sign in to report this.'
					: res.status === 400
						? 'You can change your own playlist directly.'
						: 'Could not send that report.';
		} finally {
			busy = false;
		}
	}
</script>

<Modal bind:open title="Report this playlist">
	{#snippet trigger()}
		<Dialog.Trigger class={buttonClass('secondary', 'sm')} title="Report this playlist">
			<Flag size={15} aria-hidden="true" />
			Report
		</Dialog.Trigger>
	{/snippet}

	{#if sent}
		<p class="text-sm">
			Thank you — someone who runs this instance will look at it.
		</p>
	{:else}
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			This goes to whoever runs this instance, not to the playlist's owner.
		</p>

		<Field label="What is wrong with it" class="mt-4">
			{#snippet children(f)}
				<Select id={f.id} bind:value={reason} aria-describedby={f.describedby}>
					{#each reasons as option (option.value)}
						<option value={option.value}>{option.label}</option>
					{/each}
				</Select>
			{/snippet}
		</Field>

		<Field label="Anything else" optional hint="Usually the most useful part of a report." class="mt-3">
			{#snippet children(f)}
				<Textarea id={f.id} bind:value={note} rows={3} maxlength={1000} aria-describedby={f.describedby} />
			{/snippet}
		</Field>

		{#if error}
			<p class="mt-2 text-sm" style="color: var(--color-danger)">{error}</p>
		{/if}

		<Button variant="primary" icon={Flag} class="mt-4" onclick={send} loading={busy}>
			Send report
		</Button>
	{/if}
</Modal>
