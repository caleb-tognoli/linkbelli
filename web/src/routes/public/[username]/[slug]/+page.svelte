<script lang="ts">
	import { page } from '$app/state';
	import PlaylistView from '$lib/components/PlaylistView.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const backHref = $derived(page.url.searchParams.get('from') ?? '/discover');
	const backLabel = $derived(page.url.searchParams.get('fromLabel') ?? 'Discover');

	const pageTitle = $derived(`${data.playlist.name} by ${data.username} - linkbelli`);

	// Its own description if the owner wrote one; otherwise say what the page actually holds,
	// which still beats an empty unfurl.
	const itemCount = $derived(data.playlist.itemCount);
	const description = $derived(
		data.playlist.description?.trim() ||
			`A playlist of ${itemCount} ${itemCount === 1 ? 'link' : 'links'} collected by ${data.username} on Linkbelli.`
	);

	// Canonical without the ?from tracking params the in-app back link adds.
	const canonical = $derived(
		`${page.url.origin}/public/${encodeURIComponent(data.username)}/${encodeURIComponent(data.slug)}`
	);
	const feedUrl = $derived(`${canonical}/feed`);

	// The first item that has an image stands in as the card art.
	const cardImage = $derived(
		data.items.items
			.map((i) => i.metadata?.thumbnail ?? i.link.thumbnailUrl)
			.find((src): src is string => !!src) ?? null
	);
</script>

<svelte:head>
	<title>{pageTitle}</title>
	<meta name="description" content={description} />
	<link rel="canonical" href={canonical} />

	<!-- A public playlist exists to be pasted somewhere. Without these it unfurls in Slack,
	     Discord and everywhere else as a bare URL. -->
	<meta property="og:type" content="website" />
	<meta property="og:site_name" content="Linkbelli" />
	<meta property="og:title" content={data.playlist.name} />
	<meta property="og:description" content={description} />
	<meta property="og:url" content={canonical} />
	{#if cardImage}
		<meta property="og:image" content={cardImage} />
	{/if}

	<meta name="twitter:card" content={cardImage ? 'summary_large_image' : 'summary'} />
	<meta name="twitter:title" content={data.playlist.name} />
	<meta name="twitter:description" content={description} />
	{#if cardImage}
		<meta name="twitter:image" content={cardImage} />
	{/if}

	<!-- Feed autodiscovery: a reader pointed at this page finds the feeds by itself. -->
	<link rel="alternate" type="application/rss+xml" title={`${data.playlist.name} (RSS)`} href={`${feedUrl}.rss`} />
	<link rel="alternate" type="application/atom+xml" title={`${data.playlist.name} (Atom)`} href={`${feedUrl}.atom`} />
	<link rel="alternate" type="application/feed+json" title={`${data.playlist.name} (JSON)`} href={`${feedUrl}.json`} />

	<!-- Unlisted is share-by-link: readable by anyone holding the URL, but never indexed. -->
	{#if data.playlist.visibility !== 'Public'}
		<meta name="robots" content="noindex, nofollow" />
	{/if}
</svelte:head>

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
