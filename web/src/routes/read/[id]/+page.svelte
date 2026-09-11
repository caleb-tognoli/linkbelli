<script lang="ts">
	import { ExternalLink } from '@lucide/svelte';
	import { readingMinutes } from '$lib/reading';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const minutes = $derived(readingMinutes(data.content.wordCount));
</script>

<svelte:head><title>{data.content.title ?? 'Reading'} - linkbelli</title></svelte:head>

<article class="mx-auto max-w-[38rem] pb-16">
	<header class="border-b pb-4" style="border-color: var(--color-border)">
		<h1 class="text-2xl font-semibold leading-tight">{data.content.title ?? data.content.url}</h1>
		<p class="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm" style="color: var(--color-muted)">
			<span>{data.content.siteName ?? data.content.host}</span>
			<span aria-hidden="true">·</span>
			<span>{data.content.wordCount.toLocaleString()} words</span>
			{#if minutes}
				<span aria-hidden="true">·</span>
				<span>{minutes} min read</span>
			{/if}
			<span aria-hidden="true">·</span>
			<a
				href={data.content.url}
				target="_blank"
				rel="noreferrer"
				class="inline-flex items-center gap-1 hover:underline"
			>
				Original
				<ExternalLink size={12} aria-hidden="true" />
			</a>
		</p>
	</header>

	<!-- Text only, and deliberately so: this is the copy saved at the time, not a rendering of
	     the page. Images and embeds still live on the site, which is the part that rots. -->
	<div class="mt-6 flex flex-col gap-4 text-[1.05rem] leading-relaxed">
		{#each data.content.paragraphs as paragraph, index (index)}
			<p>{paragraph}</p>
		{/each}
	</div>

	{#if data.content.truncated}
		<p class="mt-6 border-t pt-4 text-sm" style="border-color: var(--color-border); color: var(--color-muted)">
			This article was longer than linkbelli keeps. The rest is still at the
			<a href={data.content.url} target="_blank" rel="noreferrer" class="underline">original</a>.
		</p>
	{/if}
</article>
