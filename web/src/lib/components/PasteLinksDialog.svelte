<script lang="ts">
	import { Dialog } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { ClipboardPaste, X } from '@lucide/svelte';

	let {
		playlistId,
		onpasted
	}: { playlistId: string; onpasted?: () => void } = $props();

	interface PasteResult {
		found: number;
		added: number;
		alreadyThere: number;
		rejected: string[];
	}

	let open = $state(false);
	let text = $state('');
	let busy = $state(false);
	let result = $state<PasteResult | null>(null);
	let error = $state<string | null>(null);

	function reset() {
		text = '';
		result = null;
		error = null;
	}

	async function paste() {
		busy = true;
		error = null;
		try {
			const res = await api.post(`/playlists/${playlistId}/items/paste`, { text });

			if (!res.ok) {
				error =
					res.status === 400
						? 'No web addresses in that.'
						: 'Could not add those links.';
				return;
			}

			result = (await res.json()) as PasteResult;
			text = '';
			onpasted?.();
		} finally {
			busy = false;
		}
	}
</script>

<Dialog.Root bind:open onOpenChange={(value) => value && reset()}>
	<Dialog.Trigger
		class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs"
		style="border-color: var(--color-border); color: var(--color-muted)"
		title="Paste a block of links"
	>
		<ClipboardPaste size={13} aria-hidden="true" />
		Paste links
	</Dialog.Trigger>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-1/2 z-50 w-[90vw] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border p-5 shadow-xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-center justify-between">
				<Dialog.Title class="font-semibold">Paste links</Dialog.Title>
				<Dialog.Close class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10" title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>

			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				A chat log, a list of tabs, an email — anything with addresses in it. The rest of the
				text is ignored.
			</p>

			<!-- svelte-ignore a11y_autofocus -- the dialog exists to receive a paste -->
			<textarea
				autofocus
				bind:value={text}
				rows="8"
				placeholder="https://example.com/one&#10;https://example.com/two"
				aria-label="Text to take links from"
				class="mt-3 w-full rounded-md border px-3 py-2 font-mono text-sm"
				style="border-color: var(--color-border); background: var(--color-bg)"
			></textarea>

			{#if error}
				<p class="mt-2 text-sm" style="color: var(--color-danger)">{error}</p>
			{/if}

			{#if result}
				<div class="mt-2 text-sm">
					<p>
						Added {result.added} of {result.found}.
						{#if result.alreadyThere}
							{result.alreadyThere} {result.alreadyThere === 1 ? 'was' : 'were'} already here.
						{/if}
					</p>
					{#if result.rejected.length}
						<!-- Named rather than dropped: a paste that quietly loses two of forty is worse
						     than one that says which two. -->
						<p class="mt-1" style="color: var(--color-danger)">Could not read:</p>
						<ul class="mt-0.5" style="color: var(--color-muted)">
							{#each result.rejected as url (url)}
								<li class="truncate text-xs">{url}</li>
							{/each}
						</ul>
					{/if}
				</div>
			{/if}

			<button
				type="button"
				onclick={paste}
				disabled={busy || !text.trim()}
				class="mt-4 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
				style="background: var(--color-accent-solid); color: var(--color-on-solid)"
			>{busy ? 'Adding…' : 'Add them'}</button>
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
