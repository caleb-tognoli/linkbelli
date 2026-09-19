<script lang="ts">
	import BackLink from '$lib/components/ui/BackLink.svelte';
	import { page } from '$app/state';
	import { api } from '$lib/api/client';
	import { Rss } from '@lucide/svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const canonical = $derived(
		`${page.url.origin}/public/${encodeURIComponent(data.profile.username)}`
	);

	const counts = $derived(
		`${data.profile.publicPlaylistCount} ${data.profile.publicPlaylistCount === 1 ? 'playlist' : 'playlists'}` +
			` · ${data.profile.publicItemCount} ${data.profile.publicItemCount === 1 ? 'link' : 'links'}`
	);

	const joined = $derived(
		new Date(data.profile.joinedAt).toLocaleDateString(undefined, { year: 'numeric', month: 'long' })
	);

	const description = $derived(`${data.profile.username} has published ${counts} on Linkbelli.`);

	let followedByMe = $state(data.profile.followedByMe ?? false);
	let followerCount = $state(data.profile.followerCount ?? 0);
	let busy = $state(false);

	async function toggleFollow() {
		busy = true;
		try {
			const path = `/users/${encodeURIComponent(data.profile.username)}/follow`;
			const res = followedByMe ? await api.del(path) : await api.post(path);

			if (res.ok) {
				const state = (await res.json()) as { following: boolean; followerCount: number };
				followedByMe = state.following;
				followerCount = state.followerCount;
			}
		} finally {
			busy = false;
		}
	}
</script>

<svelte:head>
	<title>{data.profile.username} - linkbelli</title>
	<meta name="description" content={description} />
	<link rel="canonical" href={canonical} />

	<meta property="og:type" content="profile" />
	<meta property="og:site_name" content="Linkbelli" />
	<meta property="og:title" content={data.profile.username} />
	<meta property="og:description" content={description} />
	<meta property="og:url" content={canonical} />
	<meta name="twitter:card" content="summary" />
	<meta name="twitter:title" content={data.profile.username} />
	<meta name="twitter:description" content={description} />
</svelte:head>

<section class="mx-auto max-w-5xl">
	<BackLink href="/discover" label="Discover" />

	<header class="mt-3 flex items-start justify-between gap-4">
		<div>
			<h1 class="text-2xl font-semibold">@{data.profile.username}</h1>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				{counts} · here since {joined}{followerCount ? ` · ${followerCount} ${followerCount === 1 ? 'follower' : 'followers'}` : ''}
			</p>
		</div>
		{#if data.isLoggedIn && !data.isSelf}
			<!-- Following a person covers playlists they have not made yet, which is the whole
			     difference from following each of their lists by hand. -->
			<button
				type="button"
				onclick={toggleFollow}
				disabled={busy}
				class="inline-flex shrink-0 items-center gap-2 rounded-md border px-3 py-2 text-sm disabled:opacity-60"
				style="border-color: {followedByMe ? 'var(--color-accent)' : 'var(--color-border)'};
				       color: {followedByMe ? 'var(--color-accent)' : 'inherit'}"
				aria-pressed={followedByMe}
			>
				<Rss size={15} aria-hidden="true" />
				{followedByMe ? 'Following' : 'Follow'}
			</button>
		{/if}
	</header>

	{#if data.playlists.items.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing published yet.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				When {data.profile.username} makes a playlist public, it shows up here.
			</p>
		</div>
	{:else}
		<div class="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each data.playlists.items as playlist (playlist.slug)}
				<PlaylistCard
					href={`/public/${encodeURIComponent(playlist.ownerUsername)}/${encodeURIComponent(playlist.slug)}`}
					name={playlist.name}
					description={playlist.description}
					tags={playlist.tags}
					nsfw={playlist.nsfw}
					coverLinkId={playlist.coverLinkId}
					owner={playlist.ownerUsername}
					itemCount={playlist.itemCount}
					likeCount={playlist.likeCount}
				/>
			{/each}
		</div>
	{/if}
</section>
