<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { Flag, X } from '@lucide/svelte';

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

<Dialog.Root bind:open>
	<Dialog.Trigger
		class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs"
		style="border-color: var(--color-border); color: var(--color-muted)"
		title="Report this playlist"
	>
		<Flag size={13} aria-hidden="true" />
		Report
	</Dialog.Trigger>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-center justify-between">
				<Dialog.Title class="font-semibold">Report this playlist</Dialog.Title>
				<Dialog.Close class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10" title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>

			{#if sent}
				<p class="mt-3 text-sm">
					Thank you — someone who runs this instance will look at it.
				</p>
			{:else}
				<p class="mt-1 text-sm" style="color: var(--color-muted)">
					This goes to whoever runs this instance, not to the playlist's owner.
				</p>

				<label class="mt-4 flex flex-col gap-1 text-sm">
					<span>What is wrong with it</span>
					<select
						bind:value={reason}
						class="rounded-md border px-3 py-2 text-sm"
						style="border-color: var(--color-border-strong); background: var(--color-bg)"
					>
						{#each reasons as option (option.value)}
							<option value={option.value}>{option.label}</option>
						{/each}
					</select>
				</label>

				<label class="mt-3 flex flex-col gap-1 text-sm">
					<span>Anything else <span style="color: var(--color-muted)">(optional)</span></span>
					<!-- Usually the only useful part of a report. -->
					<textarea
						bind:value={note}
						rows="3"
						maxlength="1000"
						class="rounded-md border px-3 py-2 text-sm"
						style="border-color: var(--color-border-strong); background: var(--color-bg)"
					></textarea>
				</label>

				{#if error}
					<p class="mt-2 text-sm" style="color: var(--color-danger)">{error}</p>
				{/if}

				<button
					type="button"
					onclick={send}
					disabled={busy}
					class="mt-4 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
					style="background: var(--color-accent-solid); color: var(--color-on-solid)"
				>Send report</button>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
