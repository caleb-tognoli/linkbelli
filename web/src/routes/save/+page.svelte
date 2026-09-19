<svelte:head><title>Save a link - linkbelli</title></svelte:head>

<script lang="ts">
	import { api } from '$lib/api/client';
	import { Check, Clock, ExternalLink } from '@lucide/svelte';
	import { offlineSaves } from '$lib/offlineSaves.svelte';
	import OfflineSupportNotice from '$lib/components/OfflineSupportNotice.svelte';
	import { outcomeFor } from '$lib/offlineQueue';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let url = $state(data.url);
	let note = $state('');
	let playlistId = $state(data.playlists[0]?.id ?? '');
	let busy = $state(false);
	let saved = $state(false);
	let queued = $state(false);
	let error = $state<string | null>(null);

	async function save() {
		if (!url.trim() || !playlistId) return;

		busy = true;
		error = null;

		let res: Response;
		try {
			res = await api.post(`/playlists/${playlistId}/items`, {
				url: url.trim(),
				note: note.trim() || null
			});
		} catch {
			// The request never left. This is the case the queue exists for: the share sheet has
			// already closed behind the person, and losing the link here loses it for good.
			busy = false;
			queued = keep();
			if (!queued) error = 'No connection, and nowhere to keep this until there is one.';
			return;
		}

		busy = false;

		if (res.ok) {
			saved = true;
		} else if (res.status === 409) {
			// Already there is a normal outcome, not a failure to report as one.
			saved = true;
		} else if (res.status === 400) {
			error = 'That does not look like a web address.';
		} else if (outcomeFor(res.status) === 'offline') {
			// The same judgement the queue makes when it flushes: a server having a bad minute,
			// a rate limit, or a lapsed session are all "not yet" rather than "no". This page is
			// where the share sheet lands, so a dead end here loses the link — the sheet has
			// already closed behind the person and there is nothing to press again.
			queued = keep();
			if (!queued) error = 'Could not save that. Try again.';
		} else {
			error = 'Could not save that. Try again.';
		}
	}

	function keep(): boolean {
		return offlineSaves.add({
			playlistId,
			playlistName: savedPlaylist?.name ?? 'a playlist',
			url: url.trim(),
			note: note.trim() || null
		});
	}

	function another() {
		saved = false;
		queued = false;
		url = '';
		note = '';
	}

	const savedPlaylist = $derived(data.playlists.find((p) => p.id === playlistId));
</script>

<section class="mx-auto max-w-md">
	<h1 class="text-2xl font-semibold">Save a link</h1>

	{#if data.playlists.length === 0}
		<div class="mt-6 rounded-lg border border-dashed p-8 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">You need a playlist first.</p>
			<a href="/playlists" class="mt-2 inline-block text-sm underline underline-offset-2" style="color: var(--color-accent)">
				Make one
			</a>
		</div>
	{:else if queued}
		<!-- Not dressed up as a success and not reported as a failure: the link is kept, and the
		     honest thing is to say which of those it is. -->
		<div class="mt-6 rounded-lg border p-6 text-center" style="border-color: var(--color-border); background: var(--color-surface)">
			<Clock size={28} aria-hidden="true" class="mx-auto" style="color: var(--color-warning)" />
			<p class="mt-2 font-medium">Waiting for a connection.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				It is kept on this device and goes to {savedPlaylist?.name} as soon as you are back
				online. You can close this.
			</p>
			<div class="mt-4 flex justify-center gap-3 text-sm">
				<button type="button" onclick={another} class="underline underline-offset-2" style="color: var(--color-accent)">
					Save another
				</button>
			</div>
		</div>
	{:else if saved}
		<div class="mt-6 rounded-lg border p-6 text-center" style="border-color: var(--color-border); background: var(--color-surface)">
			<Check size={28} aria-hidden="true" class="mx-auto" style="color: var(--color-accent)" />
			<p class="mt-2 font-medium">Saved to {savedPlaylist?.name}.</p>
			<div class="mt-4 flex justify-center gap-3 text-sm">
				<a href={`/playlists/${playlistId}`} class="underline underline-offset-2" style="color: var(--color-accent)">
					Open the playlist
				</a>
				<button
					type="button"
					onclick={another}
					class="underline underline-offset-2"
					style="color: var(--color-muted)"
				>Save another</button>
			</div>
		</div>
	{:else}
		<div class="mt-5 flex flex-col gap-3">
			{#if data.title}
				<p class="text-sm" style="color: var(--color-muted)">{data.title}</p>
			{/if}

			<label class="flex flex-col gap-1 text-sm">
				Address
				<input
					bind:value={url}
					type="url"
					placeholder="https://…"
					class="rounded-md border px-3 py-2"
					style="border-color: var(--color-border); background: var(--color-bg)"
				/>
			</label>

			<label class="flex flex-col gap-1 text-sm">
				Playlist
				<select
					bind:value={playlistId}
					class="rounded-md border px-3 py-2"
					style="border-color: var(--color-border); background: var(--color-bg)"
				>
					{#each data.playlists as playlist (playlist.id)}
						<option value={playlist.id}>{playlist.name}</option>
					{/each}
				</select>
			</label>

			<label class="flex flex-col gap-1 text-sm">
				Note <span style="color: var(--color-muted)">(optional)</span>
				<textarea
					bind:value={note}
					rows="2"
					placeholder="Why you saved it…"
					class="resize-none rounded-md border px-3 py-2"
					style="border-color: var(--color-border); background: var(--color-bg)"
				></textarea>
			</label>

			{#if error}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
			{/if}

			<button
				type="button"
				onclick={save}
				disabled={busy || !url.trim()}
				class="rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
				style="background: var(--color-accent-solid); color: var(--color-on-solid)"
			>{busy ? 'Saving…' : 'Save'}</button>

			<!-- This is the screen the share sheet lands on, and the one that promises to work
			     without a connection. If it will not, here is where that has to be said. -->
			<OfflineSupportNotice compact />

			{#if url.trim()}
				<a
					href={url}
					target="_blank"
					rel="noopener noreferrer"
					class="inline-flex items-center gap-1.5 text-xs"
					style="color: var(--color-muted)"
				>
					<ExternalLink size={12} aria-hidden="true" /> Open it first
				</a>
			{/if}
		</div>
	{/if}
</section>
