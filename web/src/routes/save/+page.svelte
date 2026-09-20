<svelte:head><title>Save a link - linkbelli</title></svelte:head>

<script lang="ts">
	import Textarea from '$lib/components/ui/Textarea.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { api } from '$lib/api/client';
	import { Check, Clock, ExternalLink, Search, ListPlus } from '@lucide/svelte';
	import { offlineSaves } from '$lib/offlineSaves.svelte';
	import OfflineSupportNotice from '$lib/components/OfflineSupportNotice.svelte';
	import { outcomeFor } from '$lib/offlineQueue';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let url = $state(data.url);
	let note = $state('');

	/**
	 * Where the last link went.
	 *
	 * Saving from a share sheet is repetitive by nature — the same playlist, several times in an
	 * evening — and this page always started on whichever playlist happened to sort first, so
	 * every save needed the list opened again.
	 */
	const LAST_PLAYLIST = 'lb_save_playlist';

	function remembered(): string {
		try {
			const id = localStorage.getItem(LAST_PLAYLIST);
			if (id && data.playlists.some((p) => p.id === id)) return id;
		} catch {
			// Private windows and blocked storage: the first playlist will do.
		}
		return '';
	}

	let playlistId = $state(remembered() || data.playlists[0]?.id || '');

	/** Narrows a long list, so a person with forty playlists is not scrolling a select. */
	let filter = $state('');
	const choices = $derived(
		filter.trim()
			? data.playlists.filter((p) => p.name.toLowerCase().includes(filter.trim().toLowerCase()))
			: data.playlists
	);

	// Keeping to what is on offer: filtering past the chosen playlist would otherwise leave the
	// select showing a name that is no longer one of its options.
	$effect(() => {
		if (choices.length > 0 && !choices.some((p) => p.id === playlistId)) playlistId = choices[0].id;
	});
	let busy = $state(false);
	let saved = $state(false);
	let queued = $state(false);
	let error = $state<string | null>(null);

	async function save(event?: SubmitEvent) {
		event?.preventDefault();
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
			rememberPlaylist();
		} else if (res.status === 409) {
			// Already there is a normal outcome, not a failure to report as one.
			saved = true;
			rememberPlaylist();
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

	function rememberPlaylist() {
		try {
			localStorage.setItem(LAST_PLAYLIST, playlistId);
		} catch {
			// Nothing is lost that was not already saved.
		}
	}

	function keep(): boolean {
		rememberPlaylist();
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
		<div class="mt-6 rounded-card border border-dashed p-8 text-center">
			<p class="font-medium">You need a playlist first.</p>
			<p class="mt-1 text-sm text-muted">This window is small; the page that makes one is not.</p>
			<div class="mt-4 flex justify-center">
				<Button href="/playlists" variant="primary" icon={ListPlus} target="_blank">
					Make one
				</Button>
			</div>
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
		<!-- A form, so the keyboard's Enter — and a phone keyboard's Go — saves the link. It was a
		     div with a button, and pressing Enter in the address field did nothing at all. -->
		<form class="mt-5 flex flex-col gap-3" onsubmit={save}>
			{#if data.title}
				<p class="text-sm" style="color: var(--color-muted)">{data.title}</p>
			{/if}

			<Field label="Address">
				{#snippet children(f)}
					<Input
						id={f.id}
						bind:value={url}
						type="url"
						placeholder="https://…"
						aria-describedby={f.describedby}
					/>
				{/snippet}
			</Field>

			{#if url.trim()}
				<!-- Beside the address it belongs to, rather than below the Save button where it
				     read as an afterthought to a decision already made. -->
				<a
					href={url}
					target="_blank"
					rel="noopener noreferrer"
					class="-mt-2 inline-flex items-center gap-1.5 text-xs text-muted hover:underline"
				>
					<ExternalLink size={12} aria-hidden="true" /> Open it first
				</a>
			{/if}

			{#if data.playlists.length > 10}
				<Field label="Find a playlist" hideLabel>
					{#snippet children(f)}
						<Input
							id={f.id}
							bind:value={filter}
							icon={Search}
							placeholder="Find a playlist…"
							aria-describedby={f.describedby}
						/>
					{/snippet}
				</Field>
			{/if}

			<Field label="Playlist">
				{#snippet children(f)}
					<Select
						id={f.id}
						bind:value={playlistId}
						aria-describedby={f.describedby}
					>
					{#each choices as playlist (playlist.id)}
						<option value={playlist.id}>{playlist.name}</option>
					{/each}
					</Select>
				{/snippet}
			</Field>
			{#if choices.length === 0}
				<p class="-mt-2 text-xs text-muted">No playlist matches that.</p>
			{/if}

			<Field label="Note" optional>
				{#snippet children(f)}
					<Textarea
						id={f.id}
						bind:value={note}
						rows={2}
						placeholder="Why you saved it…"
						aria-describedby={f.describedby}
					/>
				{/snippet}
			</Field>

			{#if error}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
			{/if}

			<Button type="submit" variant="primary" icon={Check} loading={busy} disabled={!url.trim() || !playlistId}>
				{busy ? 'Saving…' : 'Save'}
			</Button>

			<!-- This is the screen the share sheet lands on, and the one that promises to work
			     without a connection. If it will not, here is where that has to be said. -->
			<OfflineSupportNotice compact />
		</form>
	{/if}
</section>
