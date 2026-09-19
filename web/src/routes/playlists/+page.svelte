<svelte:head><title>Playlists - linkbelli</title></svelte:head>

<script lang="ts">
	import type { Paged, Playlist } from '$lib/types';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import { api } from '$lib/api/client';
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import FolderCard from '$lib/components/FolderCard.svelte';
	import NewFolderDialog from '$lib/components/NewFolderDialog.svelte';
	import NewPlaylistDialog from '$lib/components/NewPlaylistDialog.svelte';
	import OnboardingChecklist from '$lib/components/OnboardingChecklist.svelte';
	import PlaylistCard from '$lib/components/PlaylistCard.svelte';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	const showFolders = $derived(data.rootFolders.length > 0);

	/**
	 * Pages fetched past the first.
	 *
	 * The page stopped at fifty with nothing to say there were more, so somebody with sixty
	 * unfiled playlists could not reach ten of them. Cleared when a new first page arrives — a
	 * different tag, say — so the two never mix.
	 */
	let tail = $state<{ items: Playlist[]; cursor: string | null } | null>(null);
	let loadingMore = $state(false);
	let seen = data.playlists;
	$effect(() => {
		if (data.playlists !== seen) {
			seen = data.playlists;
			tail = null;
		}
	});

	const playlists = $derived.by(() => {
		const first = data.playlists.items;
		const ids = new Set(first.map((p) => p.id));
		return [...first, ...(tail?.items ?? []).filter((p) => !ids.has(p.id))];
	});
	const nextCursor = $derived(tail ? tail.cursor : data.playlists.nextCursor);
	const isEmpty = $derived(playlists.length === 0 && !showFolders);

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const qs = new URLSearchParams({ unfiled: 'true', cursor: nextCursor });
			for (const t of data.activeTags) qs.append('tag', t);
			const res = await api.get(`/playlists?${qs}`);
			if (!res.ok) {
				toast.error(failureMessage(res.status, 'Could not load more playlists.'));
				return;
			}
			const page = (await res.json()) as Paged<Playlist>;
			tail = { items: [...(tail?.items ?? []), ...page.items], cursor: page.nextCursor };
		} finally {
			loadingMore = false;
		}
	}
</script>

<Page>
	<PageHeader title="Playlists">
		{#snippet actions()}
			<NewFolderDialog variant="ghost" iconOnly />
			<NewPlaylistDialog {form} />
		{/snippet}
	</PageHeader>

	<div class="mt-6 empty:hidden">
		<OnboardingChecklist usage={data.usage} dismissed={data.onboardingDismissed} />
	</div>

	{#if isEmpty}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">No folders or playlists yet.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Create a playlist to start collecting links.
			</p>
		</div>
	{:else}
		{#if showFolders}
			<div class="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
				{#each data.rootFolders as folder (folder.id)}
					<FolderCard {folder} />
				{/each}
			</div>
		{/if}

		{#if playlists.length}
			<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
				{#each playlists as playlist (playlist.id)}
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
					/>
				{/each}
			</div>
			{#if nextCursor}
				<LoadMore onclick={loadMore} loading={loadingMore} label="Show more playlists" />
			{/if}
		{/if}
	{/if}

	{#if data.shared.length}
		<!-- Kept apart from your own, on purpose: someone else's list, shared with you, is not
		     one of yours. -->
		<h2 class="mt-10 text-sm font-medium" style="color: var(--color-muted)">Shared with you</h2>
		<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each data.shared as entry (entry.playlistId)}
				<PlaylistCard
					href={`/playlists/${entry.playlistId}`}
					name={entry.name}
					owner={entry.ownerUsername}
					role={entry.role}
					itemCount={entry.itemCount}
				/>
			{/each}
		</div>
	{/if}
</Page>
