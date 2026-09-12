<script lang="ts">
	import { page } from '$app/state';
	import PlaylistView from '$lib/components/PlaylistView.svelte';
	import PublicPlaylistCard from '$lib/components/PublicPlaylistCard.svelte';
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

	// The owner's choice first; otherwise the first item that happens to have an image, which is
	// a guess standing in for a decision.
	const cardImage = $derived(
		data.playlist.coverLinkId
			? `${page.url.origin}/api/v1/thumbnails/${data.playlist.coverLinkId}`
			: (data.items.items
					.map((i) => i.metadata?.thumbnail ?? i.link.thumbnailUrl)
					.find((src): src is string => !!src) ?? null)
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

	<!-- oEmbed discovery: anything that speaks oEmbed turns a pasted link into a card. -->
	<link
		rel="alternate"
		type="application/json+oembed"
		href={`${page.url.origin}/oembed?url=${encodeURIComponent(canonical)}`}
		title={data.playlist.name}
	/>

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

{#if data.similar.length}
	<!-- Discovery otherwise ends at whatever you happened to open: there was no way from a
	     playlist you liked to the next one. -->
	<section class="mx-auto mt-10 max-w-5xl">
		<h2 class="text-sm font-medium" style="color: var(--color-muted)">More like this</h2>
		<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each data.similar as playlist (playlist.ownerUsername + '/' + playlist.slug)}
				<PublicPlaylistCard {playlist} />
			{/each}
		</div>
	</section>
{/if}
