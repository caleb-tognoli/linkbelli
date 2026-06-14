<script lang="ts">
	import PlaylistView from '$lib/components/PlaylistView.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<!-- Key by id so all interactive state resets when navigating between playlists. -->
{#key data.playlist.id}
	<PlaylistView
		playlist={data.playlist}
		items={data.items}
		attachedSources={data.attachedSources}
		ownSources={data.ownSources}
	/>
{/key}
