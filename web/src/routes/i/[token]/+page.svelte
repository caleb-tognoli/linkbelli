<script lang="ts">
	import { page } from '$app/state';
	import KindBadge from '$lib/components/KindBadge.svelte';
	import NsfwBadge from '$lib/components/NsfwBadge.svelte';
	import { readingLabel } from '$lib/reading';
	import { ExternalLink } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const title = $derived(data.item.title ?? data.item.url);

	// The note first: it is why this was sent, and it is the part no other unfurl would carry.
	const description = $derived(
		data.item.note?.trim() ||
			data.item.description?.trim() ||
			`A link shared by ${data.item.sharedBy} on Linkbelli.`
	);

	const cardImage = $derived(
		data.item.thumbnailLinkId ? `${page.url.origin}/api/v1/thumbnails/${data.item.thumbnailLinkId}` : null
	);
</script>

<svelte:head>
	<title>{title} - shared on linkbelli</title>
	<meta name="description" content={description} />
	<!-- A shared link exists to be pasted somewhere. Without these it unfurls as a bare URL and
	     the note that came with it is lost. -->
	<meta property="og:type" content="article" />
	<meta property="og:site_name" content="Linkbelli" />
	<meta property="og:title" content={title} />
	<meta property="og:description" content={description} />
	<meta property="og:url" content={page.url.href} />
	{#if cardImage}
		<meta property="og:image" content={cardImage} />
	{/if}
	<meta name="twitter:card" content={cardImage ? 'summary_large_image' : 'summary'} />
	<meta name="twitter:title" content={title} />
	<meta name="twitter:description" content={description} />
	{#if cardImage}
		<meta name="twitter:image" content={cardImage} />
	{/if}
	<!-- One shared link is not something to index; the person it was sent to has the address. -->
	<meta name="robots" content="noindex" />
</svelte:head>

<section class="mx-auto max-w-xl">
	<p class="text-sm" style="color: var(--color-muted)">
		{data.item.sharedBy} shared this with you
	</p>

	<article
		class="mt-3 overflow-hidden rounded-xl border"
		style="border-color: var(--color-border); background: var(--color-surface)"
	>
		{#if cardImage}
			<img src={cardImage} alt="" class="max-h-72 w-full object-cover" />
		{/if}

		<div class="p-5">
			<a
				href={data.item.url}
				target="_blank"
				rel="noreferrer"
				class="text-lg font-medium hover:underline"
			>
				{title}
			</a>
			{#if data.item.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}
			<KindBadge kind={data.item.kind} />

			<p class="mt-1 flex flex-wrap items-center gap-x-2 text-sm" style="color: var(--color-muted)">
				<span>{data.item.siteName ?? data.item.host}</span>
				{#if data.item.wordCount}
					<span aria-hidden="true">·</span>
					<span>{readingLabel(data.item.wordCount)} read</span>
				{/if}
			</p>

			{#if data.item.description && data.item.description !== data.item.note}
				<p class="mt-3 text-sm">{data.item.description}</p>
			{/if}

			{#if data.item.note}
				<!-- Set apart, because it is the sender talking rather than the page describing
				     itself — and it is usually the reason the link was sent at all. -->
				<blockquote
					class="mt-4 border-l-2 pl-3 text-sm italic"
					style="border-color: var(--color-accent)"
				>
					{data.item.note}
				</blockquote>
			{/if}

			<a
				href={data.item.url}
				target="_blank"
				rel="noreferrer"
				class="mt-5 inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium"
				style="background: var(--color-accent); color: var(--color-accent-contrast)"
			>
				Open it
				<ExternalLink size={14} aria-hidden="true" />
			</a>
		</div>
	</article>

	<p class="mt-4 text-center text-xs" style="color: var(--color-muted)">
		Shared with <a href="/" class="underline">Linkbelli</a>
	</p>
</section>
