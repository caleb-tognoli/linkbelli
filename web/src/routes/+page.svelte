<script lang="ts">
	import NewPlaylistDialog from '$lib/components/NewPlaylistDialog.svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import SourcesList from '$lib/components/SourcesList.svelte';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();
</script>

<div class="mx-auto flex max-w-5xl flex-col gap-12">
	<section id="playlists" class="scroll-mt-6">
		<header class="flex items-center justify-between">
			<h1 class="text-2xl font-semibold">Playlists</h1>
			<NewPlaylistDialog {form} />
		</header>

		{#if data.tags.length}
			<nav class="mt-4 flex flex-wrap items-center gap-2 text-sm">
				<a
					href="/"
					class="rounded-full border px-2.5 py-1"
					style={`border-color: var(--color-border); ${data.activeTag === null ? 'background: var(--color-accent); color: var(--color-accent-contrast)' : 'color: var(--color-muted)'}`}
				>
					All
				</a>
				{#each data.tags as tag (tag.name)}
					<a
						href={`/?tag=${encodeURIComponent(tag.name)}`}
						class="rounded-full border px-2.5 py-1"
						style={`border-color: var(--color-border); ${data.activeTag === tag.name ? 'background: var(--color-accent); color: var(--color-accent-contrast)' : 'color: var(--color-muted)'}`}
					>
						#{tag.name} <span class="opacity-70">{tag.playlistCount}</span>
					</a>
				{/each}
			</nav>
		{/if}

		{#if data.playlists.items.length === 0}
			<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
				<p class="font-medium">
					{data.activeTag ? `No playlists tagged #${data.activeTag}.` : 'No playlists yet.'}
				</p>
				<p class="mt-1 text-sm" style="color: var(--color-muted)">
					Create your first playlist to start collecting links.
				</p>
			</div>
		{:else}
			<div class="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
				{#each data.playlists.items as playlist (playlist.id)}
					<PlaylistCard {playlist} />
				{/each}
			</div>
		{/if}
	</section>

	<section id="sources" class="scroll-mt-6">
		<header class="flex items-center justify-between">
			<h2 class="text-2xl font-semibold">Sources</h2>
			<a
				href="/sources/new"
				class="rounded-md px-3 py-2 text-sm font-medium"
				style="background: var(--color-accent); color: var(--color-accent-contrast)"
			>
				New source
			</a>
		</header>
		<div class="mt-6">
			<SourcesList sources={data.sources} />
		</div>
	</section>
</div>
