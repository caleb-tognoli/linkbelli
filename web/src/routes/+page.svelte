<svelte:head><title>{data.dashboard ? 'Home - linkbelli' : 'linkbelli'}</title></svelte:head>

<script lang="ts">
	import Page from '$lib/components/ui/Page.svelte';
	import Dashboard from '$lib/components/Dashboard.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { ListMusic, Rss, Compass, Upload, LogIn, UserPlus, ArrowRight } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const features = [
		{
			Icon: ListMusic,
			title: 'Playlists',
			href: '/playlists',
			body: 'Ordered, taggable lists of links. Group them into folders, mark what you are done with, and keep each one private, unlisted, or public.'
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
			body: 'Bring links in from a browser bookmark export, a CSV, or a plain list of addresses, straight into a playlist of your choosing.'
		}
	];

	const steps = [
		{ title: 'Create a playlist', body: 'One list per topic — a reading queue, a watch list, a research trail.' },
		{ title: 'Attach a source', body: 'Point it at a feed or a page and Linkbelli adds new links as they appear.' },
		{ title: 'Share it, or do not', body: 'Publish a playlist under your username, hand out an unlisted link, or keep it to yourself.' }
	];
</script>

{#if data.dashboard}
	<Page>
		<Dashboard
			username={data.user?.username ?? null}
			upNext={data.dashboard.upNext}
			recent={data.dashboard.recent}
			feedNew={data.dashboard.feedNew}
			usage={data.dashboard.usage}
			onboardingDismissed={data.user?.onboardingDismissed ?? false}
		/>
	</Page>
{:else}
	<div class="mx-auto flex max-w-5xl flex-col gap-14">
		<section>
			<h1 class="text-3xl font-semibold">Linkbelli</h1>
			<p class="mt-3 max-w-2xl text-lg" style="color: var(--color-muted)">
				A home for the links you collect. Gather them into playlists, let sources keep those
				playlists filled on their own, and publish the ones you want to share.
			</p>

			<div class="mt-6 flex flex-wrap items-center gap-2 text-sm">
				{#if data.user}
					<Button href="/playlists" variant="primary" icon={ListMusic}>Your playlists</Button>
					<Button href="/sources" icon={Rss}>Your sources</Button>
				{:else}
					<Button href="/register" variant="primary" icon={UserPlus}>Create account</Button>
					<Button href="/login" icon={LogIn}>Sign in</Button>
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
{/if}
