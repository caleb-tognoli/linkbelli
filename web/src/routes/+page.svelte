<svelte:head><title>{data.dashboard ? pageTitle('Home') : pageTitle()}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
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
			email={data.user?.email ?? undefined}
			emailConfirmed={data.user?.emailConfirmed ?? true}
		/>
	</Page>
{:else}
	<div class="mx-auto flex max-w-5xl flex-col gap-14">
		<!-- The heading said "Linkbelli" directly under a bar that says "Linkbelli", so the first
		     line of the page was the one thing a visitor already knew. -->
		<section class="grid items-center gap-8 md:grid-cols-[1.1fr_1fr]">
			<div>
				<h1 class="t-hero">Collect links. Let sources fill your lists.</h1>
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
			</div>

			<!-- A drawing rather than a screenshot: it says the same thing — a source on the left
			     feeding a list on the right — and cannot go stale the next time a button moves. -->
			<figure class="hidden md:block">
				<svg
					viewBox="0 0 320 200"
					class="w-full rounded-card border border-border bg-surface"
					role="img"
					aria-labelledby="hero-illustration-title"
				>
					<title id="hero-illustration-title">
						A source on the left feeding new links into a playlist on the right
					</title>
					<!-- The source -->
					<rect x="14" y="58" width="86" height="84" rx="10" fill="var(--color-bg)" stroke="var(--color-border)" />
					<circle cx="40" cy="120" r="4" fill="var(--color-accent)" />
					<path d="M32 104a24 24 0 0 1 24 24" fill="none" stroke="var(--color-accent)" stroke-width="4" stroke-linecap="round" />
					<path d="M32 88a40 40 0 0 1 40 40" fill="none" stroke="var(--color-accent)" stroke-width="4" stroke-linecap="round" />
					<rect x="30" y="72" width="40" height="6" rx="3" fill="var(--color-border)" />

					<!-- What it carries across -->
					<path d="M108 100h84" fill="none" stroke="var(--color-border-strong)" stroke-width="2" stroke-dasharray="6 6" />
					<path d="M186 94l10 6-10 6z" fill="var(--color-border-strong)" />

					<!-- The playlist -->
					<rect x="204" y="34" width="102" height="132" rx="10" fill="var(--color-bg)" stroke="var(--color-border)" />
					<rect x="216" y="48" width="52" height="7" rx="3.5" fill="var(--color-text)" opacity="0.7" />
					{#each [70, 92, 114, 136] as y, i (y)}
						<rect x="216" y={y} width="20" height="14" rx="3" fill="var(--color-border)" />
						<rect x="242" y={y + 2} width={i === 0 ? 52 : 44} height="5" rx="2.5" fill="var(--color-text)" opacity="0.55" />
						<rect x="242" y={y + 10} width="30" height="4" rx="2" fill="var(--color-muted)" opacity="0.5" />
					{/each}
					<circle cx="296" cy="70" r="5" fill="var(--color-accent)" />
				</svg>
				<figcaption class="mt-2 text-xs" style="color: var(--color-muted)">
					A source on the left; the playlist it keeps filled on the right.
				</figcaption>
			</figure>
		</section>

		<section>
			<h2 class="text-xl font-semibold">What is in here</h2>
			<div class="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2">
				{#each features as feature (feature.href)}
					<a
						href={feature.href}
						class="group flex flex-col rounded-card border p-5 transition-colors hover:bg-black/5 dark:hover:bg-white/10"
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
