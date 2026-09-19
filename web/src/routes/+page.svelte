<svelte:head><title>linkbelli</title></svelte:head>

<script lang="ts">
	import { ListMusic, Rss, Compass, Upload, LogIn, UserPlus, ArrowRight } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const features = [
		{
			Icon: ListMusic,
			title: 'Playlists',
			href: '/playlists',
			body: 'Ordered, taggable lists of links. Group them into folders, mark what you have already watched, and keep each one private, unlisted, or public.'
		},
		{
			Icon: Rss,
			title: 'Sources',
			href: '/sources',
			body: 'RSS feeds and page scrapers that run on a schedule and drop new links straight into a playlist, so a list keeps filling itself.'
		},
		{
			Icon: Compass,
			title: 'Discover',
			href: '/discover',
			body: 'Search public playlists other people have published, filter them by tag, and subscribe to the ones worth following.'
		},
		{
			Icon: Upload,
			title: 'Import',
			href: '/import',
			body: 'Bring links in from a browser bookmark export or a plain list of URLs, straight into a playlist of your choosing.'
		}
	];

	const steps = [
		{ title: 'Create a playlist', body: 'One list per topic — a reading queue, a watch list, a research trail.' },
		{ title: 'Attach a source', body: 'Point it at a feed or a page and Linkbelli adds new links as they appear.' },
		{ title: 'Share it, or do not', body: 'Publish a playlist under your username, hand out an unlisted link, or keep it to yourself.' }
	];
</script>

<div class="mx-auto flex max-w-5xl flex-col gap-14">
	<section>
		<h1 class="text-3xl font-semibold">Linkbelli</h1>
		<p class="mt-3 max-w-2xl text-lg" style="color: var(--color-muted)">
			A home for the links you collect. Gather them into playlists, let sources keep those
			playlists filled on their own, and publish the ones you want to share.
		</p>

		<div class="mt-6 flex flex-wrap items-center gap-2 text-sm">
			{#if data.user}
				<a
					href="/playlists"
					class="inline-flex items-center gap-1.5 rounded-md px-3 py-2 font-medium"
					style="background: var(--color-accent-solid); color: var(--color-on-solid)"
				>
					<ListMusic size={18} aria-hidden="true" /> Your playlists
				</a>
				<a
					href="/sources"
					class="inline-flex items-center gap-1.5 rounded-md border px-3 py-2 font-medium hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
				>
					<Rss size={18} aria-hidden="true" /> Your sources
				</a>
			{:else}
				<a
					href="/register"
					class="inline-flex items-center gap-1.5 rounded-md px-3 py-2 font-medium"
					style="background: var(--color-accent-solid); color: var(--color-on-solid)"
				>
					<UserPlus size={18} aria-hidden="true" /> Create account
				</a>
				<a
					href="/login"
					class="inline-flex items-center gap-1.5 rounded-md border px-3 py-2 font-medium hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
				>
					<LogIn size={18} aria-hidden="true" /> Sign in
				</a>
			{/if}
		</div>
	</section>

	<section>
		<h2 class="text-xl font-semibold">What is in here</h2>
		<div class="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2">
			{#each features as feature (feature.href)}
				<a
					href={feature.href}
					class="group flex flex-col rounded-lg border p-5 transition-colors hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<span class="flex items-center gap-2.5">
						<feature.Icon size={20} aria-hidden="true" />
						<span class="font-medium">{feature.title}</span>
						<ArrowRight
							size={16}
							aria-hidden="true"
							class="opacity-0 transition-opacity group-hover:opacity-100"
						/>
					</span>
					<span class="mt-2 text-sm" style="color: var(--color-muted)">{feature.body}</span>
				</a>
			{/each}
		</div>
	</section>

	<section>
		<h2 class="text-xl font-semibold">How it fits together</h2>
		<ol class="mt-5 flex flex-col gap-4">
			{#each steps as step, i (step.title)}
				<li class="flex gap-4">
					<span
						class="flex size-7 shrink-0 items-center justify-center rounded-full text-sm font-medium"
						style="background: var(--color-border)"
						aria-hidden="true"
					>
						{i + 1}
					</span>
					<span class="min-w-0">
						<span class="block font-medium">{step.title}</span>
						<span class="mt-0.5 block text-sm" style="color: var(--color-muted)">{step.body}</span>
					</span>
				</li>
			{/each}
		</ol>
	</section>
</div>
