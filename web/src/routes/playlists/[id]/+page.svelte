<script lang="ts">
	import { page } from '$app/state';
	import PlaylistView from '$lib/components/PlaylistView.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const backHref = $derived(page.url.searchParams.get('from') ?? undefined);
	const backLabel = $derived(page.url.searchParams.get('fromLabel') ?? undefined);
</script>

<!-- Key by id so all interactive state resets when navigating between playlists. -->
{#key data.playlist.id}
	<PlaylistView
		playlist={data.playlist}
		items={data.items}
		attachedSources={data.attachedSources}
		ownSources={data.ownSources}
		{backHref}
		{backLabel}
	/>
{/key}
