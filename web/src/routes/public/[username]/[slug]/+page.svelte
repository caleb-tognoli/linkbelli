<script lang="ts">
	import { page } from '$app/state';
	import PlaylistView from '$lib/components/PlaylistView.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const backHref = $derived(page.url.searchParams.get('from') ?? '/discover');
	const backLabel = $derived(page.url.searchParams.get('fromLabel') ?? 'Discover');
</script>

<svelte:head><title>{data.playlist.name} by {data.username} - linkbelli</title></svelte:head>

{#key data.playlist.id}
	<PlaylistView
		playlist={data.playlist}
		items={data.items}
		attachedSources={data.attachedSources}
		isOwner={false}
		isLoggedIn={!!data.user}
		ownerUsername={data.username}
		initialPrefs={data.initialPrefs}
		{backHref}
		{backLabel}
	/>
{/key}
