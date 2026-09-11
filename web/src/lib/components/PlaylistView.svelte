<script lang="ts">
	import PlaylistSearchBar from './PlaylistSearchBar.svelte';
	import LinkTable from './LinkTable.svelte';
	import TagEditor from './TagEditor.svelte';
	import SourcesPanel from './SourcesPanel.svelte';
	import SaveToFolderDialog from './SaveToFolderDialog.svelte';
	import ShareWithDialog from './ShareWithDialog.svelte';
	import PasteLinksDialog from './PasteLinksDialog.svelte';
	import ReportDialog from './ReportDialog.svelte';
	import { Popover } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { savePrefs } from '$lib/prefs';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { goto } from '$app/navigation';
	import { ChevronDown, Download, EyeOff, Globe, Heart, Lock, Rss, Star, Trash2 } from '@lucide/svelte';
	import type { AttachedSource, NsfwSetting, Paged, Playlist, PlaylistItem, PlaylistRole, SourceSummary, Visibility } from '$lib/types';
	import type { PlaylistPrefs } from '$lib/prefs';

	type VisOption = { label: string; icon: typeof Lock };
	const visConfig: Record<Visibility, VisOption> = {
		Private: { label: 'Private', icon: Lock },
		Unlisted: { label: 'Unlisted', icon: EyeOff },
		Public: { label: 'Public', icon: Globe }
	};

	let {
		playlist,
		items: itemsPage,
		attachedSources,
		ownSources = [],
		isOwner = true,
		role = undefined,
		isLoggedIn = true,
		ownerUsername = undefined,
		backHref = undefined,
		backLabel = undefined,
		initialPrefs = undefined
	}: {
		playlist: Playlist;
		items: Paged<PlaylistItem>;
		attachedSources: AttachedSource[];
		ownSources?: SourceSummary[];
		isOwner?: boolean;
		/** What a non-owner may do here, when this playlist was shared with them. */
		role?: PlaylistRole;
		isLoggedIn?: boolean;
		ownerUsername?: string;
		backHref?: string;
		backLabel?: string;
		initialPrefs?: PlaylistPrefs;
	} = $props();

	// Adding and editing are deliberately different permissions: "help me collect things" should
	// not also mean "delete things".
	const canAdd = $derived(isOwner || role === 'Contributor' || role === 'Editor');
	const canEdit = $derived(isOwner || role === 'Editor');

	// Held locally so the button responds at once rather than after a round trip and a reload.
	let likeCount = $state(playlist.likeCount ?? 0);
	let likedByMe = $state(playlist.likedByMe ?? false);
	let liking = $state(false);

	let followerCount = $state(playlist.followerCount ?? 0);
	let followedByMe = $state(playlist.followedByMe ?? false);
	let following = $state(false);

	// Liking says "this is good"; following says "tell me when there is more". They are
	// different questions, so they are different buttons.
	async function toggleFollow() {
		if (!isLoggedIn) return;

		following = true;
		try {
			const res = followedByMe
				? await api.del(`/playlists/${playlist.id}/follow`)
				: await api.post(`/playlists/${playlist.id}/follow`);

			if (res.ok) {
				const state = (await res.json()) as { following: boolean; followerCount: number };
				followedByMe = state.following;
				followerCount = state.followerCount;
			}
		} finally {
			following = false;
		}
	}

	async function toggleLike() {
		if (!isLoggedIn) return;

		liking = true;
		try {
			const res = likedByMe
				? await api.del(`/playlists/${playlist.id}/like`)
				: await api.post(`/playlists/${playlist.id}/like`);

			if (res.ok) {
				// The server's own count, not an increment: two tabs, or a double tap, would
				// otherwise drift away from the real number.
				const state = (await res.json()) as { likeCount: number; likedByMe: boolean };
				likeCount = state.likeCount;
				likedByMe = state.likedByMe;
			}
		} finally {
			liking = false;
		}
	}

	const resolvedBackHref = $derived(backHref ?? (isOwner ? '/playlists' : '/discover'));
	const resolvedBackLabel = $derived(backLabel ?? (isOwner ? 'Playlists' : 'Discover'));

	type StatusFilter = 'All' | 'Unwatched' | 'Watched';
	const defaultStatus: StatusFilter = isOwner ? 'Unwatched' : 'All';

	let items = $state(itemsPage.items);
	let nextCursor = $state(itemsPage.nextCursor);
	let total = $state<number | null>(itemsPage.total ?? null);
	let serverSort = $state(initialPrefs?.sort ?? 'position');
	let sourceFilter = $state<string | null>(initialPrefs?.source ?? null);
	let statusFilter = $state<StatusFilter>((initialPrefs?.status as StatusFilter | null) ?? defaultStatus);
	let query = $state('');
	let queryInitialized = false;
	let tags = $state(playlist.tags);
	let attached = $state(attachedSources);
	let loadingMore = $state(false);
	let visibility = $state(playlist.visibility);
	let visOpen = $state(false);
	let exportOpen = $state(false);
	let nsfwOpen = $state(false);

	// Adult detection reads a meta tag the site declares about itself, so it gets false
	// positives — and a wrongly flagged playlist is hidden from everyone who hasn't opted in.
	let nsfwSetting = $state<NsfwSetting>(playlist.nsfwSetting ?? 'Auto');
	let isNsfw = $state(playlist.nsfw);

	const NSFW_LABELS: Record<NsfwSetting, string> = {
		Auto: 'Automatic',
		Yes: 'Adult',
		No: 'Not adult'
	};

	async function setNsfw(next: NsfwSetting) {
		const previous = nsfwSetting;
		nsfwSetting = next;
		nsfwOpen = false;

		const res = await api.patch(`/playlists/${playlist.id}`, { nsfw: next });
		if (res.ok) {
			isNsfw = ((await res.json()) as Playlist).nsfw;
		} else {
			nsfwSetting = previous;
		}
	}

	const PLAYLIST_EXPORTS = [
		{ format: 'json', label: 'JSON' },
		{ format: 'csv', label: 'CSV' },
		{ format: 'html', label: 'Bookmarks' }
	];
	let playlistName = $state(playlist.name);
	const currentVis = $derived(visConfig[visibility] ?? visConfig.Private);

	async function saveName(el: HTMLInputElement) {
		const name = el.value.trim();
		if (!name) { el.value = playlistName; return; }
		if (name === playlistName) return;
		const res = await api.patch(`/playlists/${playlist.id}`, { name });
		if (res.ok) playlistName = name;
		else el.value = playlistName;
	}

	async function deletePlaylist() {
		if (!(await confirmDialog(`Delete "${playlistName}"? This cannot be undone.`, { danger: true, confirmLabel: 'Delete' }))) return;
		const res = await api.del(`/playlists/${playlist.id}`);
		if (res.ok || res.status === 204) goto(resolvedBackHref);
	}

	async function setVisibility(next: Visibility) {
		const prev = visibility;
		visibility = next;
		const res = await api.patch(`/playlists/${playlist.id}`, { visibility: next });
		if (!res.ok) visibility = prev;
	}

	function onAdded(item: PlaylistItem) {
		items = [...items, item];
		if (total !== null) total = total + 1;
	}

	function itemsEndpoint() {
		return isOwner
			? `/playlists/${playlist.id}/items`
			: `/public/playlists/${encodeURIComponent(ownerUsername!)}/${encodeURIComponent(playlist.slug)}/items`;
	}

	function buildParams(extra: Record<string, string> = {}) {
		const p = new URLSearchParams(extra);
		if (serverSort !== 'position') p.set('sort', serverSort);
		if (sourceFilter !== null) p.set('source', sourceFilter);
		if (statusFilter !== 'All') p.set('status', statusFilter.toLowerCase());
		if (query.trim()) p.set('q', query.trim());
		const qs = p.toString();
		return qs ? `?${qs}` : '';
	}

	$effect(() => {
		const term = query;
		if (!queryInitialized) {
			queryInitialized = true;
			return;
		}
		const delay = term ? 300 : 0;
		const t = setTimeout(reloadItems, delay);
		return () => clearTimeout(t);
	});

	function applyPage(page: Paged<PlaylistItem>) {
		items = page.items;
		nextCursor = page.nextCursor;
		total = page.total ?? null;
	}

	async function reloadItems() {
		const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
		if (res.ok) applyPage((await res.json()) as Paged<PlaylistItem>);
	}

	async function onfetchsort(sort: string) {
		serverSort = sort;
		savePrefs(playlist.id, { sort });
		nextCursor = null; // clear stale cursor immediately while loading
		const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
		if (res.ok) applyPage((await res.json()) as Paged<PlaylistItem>);
	}

	async function applySourceFilter(source: string | null) {
		sourceFilter = source;
		savePrefs(playlist.id, { source });
		nextCursor = null;
		const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
		if (res.ok) applyPage((await res.json()) as Paged<PlaylistItem>);
	}

	async function applyStatusFilter(status: StatusFilter) {
		statusFilter = status;
		savePrefs(playlist.id, { status });
		nextCursor = null;
		const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
		if (res.ok) applyPage((await res.json()) as Paged<PlaylistItem>);
	}

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const res = await api.get(`${itemsEndpoint()}${buildParams({ cursor: nextCursor })}`);
			if (res.ok) {
				const page = (await res.json()) as Paged<PlaylistItem>;
				items = [...items, ...page.items];
				nextCursor = page.nextCursor;
				if (page.total !== undefined) total = page.total;
			}
		} finally {
			loadingMore = false;
		}
	}
</script>

<section class="mx-auto max-w-5xl">
	<a
		href={resolvedBackHref}
		class="inline-flex items-center gap-1.5 text-sm"
		style="color: var(--color-muted)"
	>
		← {resolvedBackLabel}
	</a>

	<header class="mt-3 flex items-start justify-between gap-3">
		<div class="min-w-0">
			{#if isOwner}
				<input
					type="text"
					value={playlistName}
					class="w-full bg-transparent text-2xl font-semibold outline-none focus-visible:!outline-none"
					style="min-width: 0"
					onblur={(e) => saveName(e.currentTarget)}
					onkeydown={(e) => { if (e.key === 'Enter') e.currentTarget.blur(); if (e.key === 'Escape') { e.currentTarget.value = playlistName; e.currentTarget.blur(); } }}
				/>
			{:else}
				<h1 class="text-2xl font-semibold">{playlistName}</h1>
			{/if}
			{#if ownerUsername}
				<p class="mt-0.5 text-sm" style="color: var(--color-muted)">
					by <a href={`/public/${encodeURIComponent(ownerUsername)}`} class="hover:underline">@{ownerUsername}</a>
				</p>
			{/if}
			{#if playlist.description}
				<p class="mt-1" style="color: var(--color-muted)">{playlist.description}</p>
			{/if}
		</div>
		<div class="flex shrink-0 items-center gap-2">
			{#if isOwner}
				<Popover.Root bind:open={visOpen}>
					<Popover.Trigger
						class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:border-[var(--color-accent)]"
						style="border-color: var(--color-border)"
						title="Change visibility"
						aria-label="Visibility"
					>
						<currentVis.icon size={15} aria-hidden="true" />
						{currentVis.label}
					</Popover.Trigger>
					<Popover.Content
						class="popover-surface z-30 rounded-md border shadow-md overflow-hidden"
						sideOffset={4}
						align="end"
					>
						{#each Object.entries(visConfig) as [val, { label, icon: Icon }] (val)}
							<button
								type="button"
								onclick={() => { setVisibility(val as Visibility); visOpen = false; }}
								class="flex w-full items-center gap-2 px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
								class:font-medium={visibility === val}
							>
								<Icon size={15} aria-hidden="true" style="color: var(--color-muted)" />
								{label}
							</button>
						{/each}
					</Popover.Content>
				</Popover.Root>
			{/if}
			{#if !isOwner}
				<!-- The lightest thing a visitor can say about someone else's list, and the only
				     thing they do here that its owner ever sees. Shown to anonymous visitors as a
				     count they can read but not add to. -->
				<button
					type="button"
					onclick={toggleLike}
					disabled={!isLoggedIn || liking}
					class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs disabled:cursor-default"
					style="border-color: {likedByMe ? 'var(--color-accent)' : 'var(--color-border)'};
					       color: {likedByMe ? 'var(--color-accent)' : 'var(--color-muted)'}"
					title={isLoggedIn
						? (likedByMe ? 'You like this' : 'Like this playlist')
						: 'Sign in to like this'}
					aria-pressed={likedByMe}
				>
					<Heart size={13} aria-hidden="true" fill={likedByMe ? 'currentColor' : 'none'} />
					{likeCount}
				</button>
			{/if}
			{#if !isOwner && isLoggedIn}
				<button
					type="button"
					onclick={toggleFollow}
					disabled={following}
					class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs disabled:opacity-60"
					style="border-color: {followedByMe ? 'var(--color-accent)' : 'var(--color-border)'};
					       color: {followedByMe ? 'var(--color-accent)' : 'var(--color-muted)'}"
					aria-pressed={followedByMe}
					title={followedByMe ? 'New links here reach your feed' : 'Get new links from this list in your feed'}
				>
					<Rss size={13} aria-hidden="true" />
					{followedByMe ? 'Following' : 'Follow'}{followerCount ? ` · ${followerCount}` : ''}
				</button>
			{/if}
			{#if canAdd}
				<PasteLinksDialog playlistId={playlist.id} onpasted={reloadItems} />
			{/if}
			{#if isOwner}
				<ShareWithDialog playlistId={playlist.id} />
			{/if}
			{#if !isOwner && isLoggedIn && ownerUsername}
				<!-- Moderation was a host blocklist and nothing else: someone who found a problem
				     had no way to say so. -->
				<ReportDialog username={ownerUsername} slug={playlist.slug} />
			{/if}
			{#if isLoggedIn}
				<SaveToFolderDialog
					playlistId={playlist.id}
					currentFolderId={playlist.folderId}
					currentFolderName={playlist.folderName}
				/>
			{/if}
			{#if isOwner && (isNsfw || nsfwSetting !== 'Auto')}
				<Popover.Root bind:open={nsfwOpen}>
					<Popover.Trigger
						class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs"
						style="border-color: {isNsfw ? 'var(--color-danger)' : 'var(--color-border)'};
						       color: {isNsfw ? 'var(--color-danger)' : 'var(--color-muted)'}"
						title="Adult content"
					>
						{isNsfw ? 'Adult' : 'Not adult'}
						<ChevronDown size={13} aria-hidden="true" />
					</Popover.Trigger>
					<Popover.Content
						class="popover-surface z-30 w-56 overflow-hidden rounded-md border shadow-md"
						sideOffset={4}
						align="end"
					>
						{#each ['Auto', 'No', 'Yes'] as const as option (option)}
							<button
								type="button"
								onclick={() => setNsfw(option)}
								class="flex w-full flex-col items-start px-3 py-2 text-left text-sm hover:bg-black/5 dark:hover:bg-white/10"
								class:font-medium={nsfwSetting === option}
							>
								{NSFW_LABELS[option]}
								{#if option === 'Auto'}
									<span class="text-xs" style="color: var(--color-muted)">Go by what the sites declare</span>
								{/if}
							</button>
						{/each}
					</Popover.Content>
				</Popover.Root>
			{/if}
			{#if isOwner}
				<Popover.Root bind:open={exportOpen}>
					<Popover.Trigger
						class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						title="Export this playlist"
						aria-label="Export this playlist"
					>
						<Download size={17} aria-hidden="true" />
					</Popover.Trigger>
					<Popover.Content
						class="popover-surface z-30 overflow-hidden rounded-md border shadow-md"
						sideOffset={4}
						align="end"
					>
						{#each PLAYLIST_EXPORTS as fmt (fmt.format)}
							<a
								href={`/api/v1/export/playlists/${playlist.id}?format=${fmt.format}`}
								download
								onclick={() => (exportOpen = false)}
								class="flex w-full items-center gap-2 px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
							>
								<Download size={14} aria-hidden="true" style="color: var(--color-muted)" />
								{fmt.label}
							</a>
						{/each}
					</Popover.Content>
				</Popover.Root>
				<button
					type="button"
					onclick={deletePlaylist}
					class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					style="color: var(--color-danger)"
					title="Delete playlist"
					aria-label="Delete playlist"
				>
					<Trash2 size={17} aria-hidden="true" />
				</button>
			{/if}
		</div>
	</header>

	{#if isOwner && playlist.averageScore != null && playlist.scoredCount}
		<p class="mt-2 flex items-center gap-1.5 text-sm" style="color: var(--color-muted)">
			<Star size={14} aria-hidden="true" />
			<span class="tabular-nums">{playlist.averageScore.toFixed(0)}</span>
			average across {playlist.scoredCount} rated
			{playlist.scoredCount === 1 ? 'link' : 'links'}
		</p>
	{/if}

	{#if isOwner && playlist.pendingCount}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			{playlist.pendingCount} more {playlist.pendingCount === 1 ? 'link is' : 'links are'} being fetched — they
			appear here once their pages have been read.
		</p>
	{/if}

	<div class="mt-3">
		<TagEditor playlistId={playlist.id} bind:tags readonly={!canEdit} />
	</div>

	{#if isOwner || attached.length > 0}
		<div class="mt-3">
			<SourcesPanel
				playlistId={playlist.id}
				bind:attached
				{ownSources}
				{isOwner}
				{isLoggedIn}
				fromHref={isOwner ? `/playlists/${playlist.id}` : undefined}
				fromLabel={isOwner ? playlist.name : undefined}
				onreloaditems={isOwner ? reloadItems : undefined}
			/>
		</div>
	{/if}

	<div class="mt-5">
		<PlaylistSearchBar
			playlistId={playlist.id}
			isOwner={canAdd}
			bind:query
			resultCount={items.length}
			{onAdded}
		/>
	</div>

	<div class="mt-5">
		<LinkTable
			bind:items
			readonly={!canEdit}
			{onfetchsort}
			onmove={reloadItems}
			playlistId={playlist.id}
			{initialPrefs}
			attachedSources={attached}
			{sourceFilter}
			onsourcefilter={applySourceFilter}
			{statusFilter}
			onstatusfilter={applyStatusFilter}
			isSearching={query.trim().length > 0}
			bind:total
		/>
		{#if nextCursor}
			<div class="mt-3 text-center">
				<button
					type="button"
					onclick={loadMore}
					disabled={loadingMore}
					class="rounded-md border p-1.5 disabled:opacity-60"
					style="border-color: var(--color-border)"
					title="Load more"
					aria-label="Load more"
				>
					<ChevronDown size={18} aria-hidden="true" />
				</button>
			</div>
		{/if}
	</div>
</section>
