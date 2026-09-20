<script lang="ts" module>
	/** What "make a new one" is worth in the select, since a playlist id never looks like this. */
	export const NEW_PLAYLIST = 'new';

	/**
	 * Makes the playlist a new source was told to fill, when it does not exist yet.
	 *
	 * Returns its id, or null when the request failed — the caller decides whether that is worth
	 * stopping for.
	 */
	export async function createPlaylist(name: string): Promise<string | null> {
		const res = await api.post('/playlists', { name: name.trim(), visibility: 'Private' });
		if (!res.ok) return null;

		return ((await res.json()) as { id: string }).id;
	}
</script>

<script lang="ts">
	import Field from '$lib/components/ui/Field.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import { api } from '$lib/api/client';
	import type { Playlist } from '$lib/types';

	/**
	 * Where what a source finds should land.
	 *
	 * A source with no playlist runs on schedule, fetches everything and puts it nowhere anybody
	 * would look — which is the whole promise of the product, missing by one step.
	 */
	let {
		playlists,
		value = $bindable(),
		newName = $bindable(''),
		error = null
	}: {
		playlists: Playlist[];
		/** A playlist id, or NEW_PLAYLIST. */
		value: string;
		/** The name to give the new playlist, when that is what was chosen. */
		newName?: string;
		error?: string | null;
	} = $props();
</script>

<Field
	label="Put what it finds in"
	hint="Every link this source brings in goes here."
	required
	{error}
>
	{#snippet children(f)}
		<Select id={f.id} bind:value invalid={f.invalid} aria-describedby={f.describedby}>
			{#each playlists as playlist (playlist.id)}
				<option value={playlist.id}>{playlist.name}</option>
			{/each}
			<option value={NEW_PLAYLIST}>New playlist…</option>
		</Select>
	{/snippet}
</Field>

{#if value === NEW_PLAYLIST}
	<Field label="Name of the new playlist" required>
		{#snippet children(f)}
			<Input
				id={f.id}
				bind:value={newName}
				placeholder="What this source feeds"
				required
				aria-describedby={f.describedby}
			/>
		{/snippet}
	</Field>
{/if}
