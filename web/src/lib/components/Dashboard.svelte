<script lang="ts">
	import OnboardingChecklist from './OnboardingChecklist.svelte';
	import SiteMark from '$lib/components/SiteMark.svelte';
	import ConfirmEmailNotice from './ConfirmEmailNotice.svelte';
	import { ArrowRight, BookOpen, Link2, ListMusic, Rss } from '@lucide/svelte';
	import Button from './ui/Button.svelte';
	import PageHeader from './ui/PageHeader.svelte';
	import PlaylistCard from './PlaylistCard.svelte';
	import { readingLabel } from '$lib/reading';
	import type { Paged, Playlist, SearchHit, Usage } from '$lib/types';

	/**
	 * What somebody signed in lands on: their own things, rather than an introduction to the
	 * app they are already using.
	 */
	let {
		username,
		upNext,
		recent,
		feedNew,
		usage,
		onboardingDismissed = false,
		email = undefined,
		emailConfirmed = true
	}: {
		username: string | null;
		upNext: Paged<SearchHit>;
		recent: Playlist[];
		feedNew: number;
		usage: Usage | null;
		onboardingDismissed?: boolean;
		email?: string;
		/** Until this is true, nothing the app sends can reach them. */
		emailConfirmed?: boolean;
	} = $props();

	/**
	 * Whether anything has ever been saved here.
	 *
	 * Registration signs somebody in and drops them on this page, so the first sentence a new
	 * account read was "Welcome back" — half a minute after it was made — over an Up next that
	 * said everything they had saved was marked done, which was true only because they had saved
	 * nothing. Both were written for the case that is common later, not the one that is certain
	 * first.
	 */
	const isNew = $derived(!usage || usage.items + usage.pendingItems === 0);
</script>

<div class="flex flex-col gap-10">
	<PageHeader
		title={isNew
			? username
				? `Welcome, ${username}`
				: 'Welcome'
			: username
				? `Welcome back, ${username}`
				: 'Welcome back'}
	>
		{#snippet actions()}
			<Button href="/save" icon={Link2}>Save a link</Button>
			<Button href="/playlists" variant="primary" icon={ListMusic}>Your playlists</Button>
		{/snippet}
	</PageHeader>

	{#if !emailConfirmed}
		<ConfirmEmailNotice {email} />
	{/if}

	<!-- First, for a new account: this is the screen it lands on after signing up, so the
	     first steps belong here rather than one click away on the playlists page. -->
	<OnboardingChecklist {usage} dismissed={onboardingDismissed} />

	{#if feedNew > 0}
		<a
			href="/feed"
			class="flex items-center gap-3 rounded-card border border-accent bg-selected px-4 py-3 text-sm hover:underline"
		>
			<Rss size={18} aria-hidden="true" class="text-accent" />
			<span class="flex-1">
				{feedNew} new {feedNew === 1 ? 'link' : 'links'} from what you follow
			</span>
			<ArrowRight size={16} aria-hidden="true" />
		</a>
	{/if}

	<section aria-labelledby="dash-next">
		<div class="flex items-baseline justify-between gap-3">
			<h2 id="dash-next" class="t-section">Up next</h2>
			{#if upNext.items.length}
				<a href="/queue" class="text-sm text-muted hover:text-text hover:underline">
					All {upNext.total ?? ''} waiting
				</a>
			{/if}
		</div>
		{#if upNext.items.length === 0}
			<p class="mt-2 text-sm text-muted">
				{#if isNew}
					Nothing here yet. <a href="/save" class="text-accent underline underline-offset-2">Save a link</a>
					and it turns up in Up next.
				{:else}
					Nothing waiting — everything you saved is marked done.
				{/if}
			</p>
		{:else}
			<ul class="mt-3 flex flex-col divide-y rounded-card border border-border">
				{#each upNext.items as hit (hit.itemId)}
					<li class="flex items-start gap-3 p-3">
						<SiteMark src={hit.link.favicon} class="mt-0.5" />
						<div class="min-w-0 flex-1">
							<a
								href={hit.link.url}
								target="_blank"
								rel="noopener noreferrer"
								class="break-words font-medium hover:underline"
							>
								{hit.link.title ?? hit.link.url}<span class="sr-only"> (opens in a new tab)</span></a>
							<p class="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-muted">
								<a href={`/playlists/${hit.playlistId}`} class="hover:underline">{hit.playlistName}</a>
								{#if hit.link.wordCount}
									<span aria-hidden="true">·</span>
									<a
										href={`/read/${hit.link.id}?from=${hit.playlistId}`}
										class="inline-flex items-center gap-1 hover:underline"
									>
										<BookOpen size={12} aria-hidden="true" />
										{readingLabel(hit.link.wordCount)} read
									</a>
								{/if}
							</p>
						</div>
					</li>
				{/each}
			</ul>
		{/if}
	</section>

	<section aria-labelledby="dash-recent">
		<div class="flex items-baseline justify-between gap-3">
			<h2 id="dash-recent" class="t-section">Recent playlists</h2>
			{#if recent.length}
				<a href="/playlists" class="text-sm text-muted hover:text-text hover:underline">All playlists</a>
			{/if}
		</div>
		{#if recent.length === 0}
			<p class="mt-2 text-sm text-muted">
				No playlists yet. <a href="/playlists" class="text-accent underline underline-offset-2">Make the first one</a>.
			</p>
		{:else}
			<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
				{#each recent as playlist (playlist.id)}
					<PlaylistCard
						href={`/playlists/${playlist.id}`}
						name={playlist.name}
						description={playlist.description}
						tags={playlist.tags}
						nsfw={playlist.nsfw}
						coverLinkId={playlist.coverLinkId}
						visibility={playlist.visibility}
						itemCount={playlist.itemCount}
						pendingCount={playlist.pendingCount}
						tagHref={(tag) => `/playlists?tag=${encodeURIComponent(tag)}`}
					/>
				{/each}
			</div>
		{/if}
	</section>

	{#if usage}
		<p class="text-xs text-muted">
			{usage.playlists} {usage.playlists === 1 ? 'playlist' : 'playlists'} · {usage.items}
			{usage.items === 1 ? 'link' : 'links'} · {usage.sources}
			{usage.sources === 1 ? 'source' : 'sources'}
		</p>
	{/if}
</div>
