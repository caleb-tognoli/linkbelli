<script lang="ts">
	import { scrollBehavior } from '$lib/motion';
	import { tick } from 'svelte';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import BackLink from '$lib/components/ui/BackLink.svelte';
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
	import MenuRadio from '$lib/components/ui/MenuRadio.svelte';
	import MenuItem from '$lib/components/ui/MenuItem.svelte';
	import Menu from '$lib/components/ui/Menu.svelte';
	import Button, { buttonClass } from '$lib/components/ui/Button.svelte';
	import PlaylistSearchBar from './PlaylistSearchBar.svelte';
	import LinkTable from './LinkTable.svelte';
	import TagEditor from './TagEditor.svelte';
	import SourcesPanel from './SourcesPanel.svelte';
	import SaveToFolderDialog from './SaveToFolderDialog.svelte';
	import ShareWithDialog from './ShareWithDialog.svelte';
	import PasteLinksDialog from './PasteLinksDialog.svelte';
	import ReportDialog from './ReportDialog.svelte';
	import { api } from '$lib/api/client';
	import { savePrefs } from '$lib/prefs';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { ChevronDown, CopyPlus, Download, Eye, EyeOff, Globe, Heart, Lock, Rss, Star, Trash2 } from '@lucide/svelte';
	import type { AttachedSource, NsfwSetting, Paged, Playlist, PlaylistItem, PlaylistRole, SourceSummary, Visibility } from '$lib/types';
	import type { PlaylistPrefs } from '$lib/prefs';

	type VisOption = { label: string; icon: typeof Lock };
	/** What each visibility means, said where it is chosen. */
	const VIS_HINTS: Record<Visibility, string> = {
		Private: 'Only you, and anyone you share it with',
		Unlisted: 'Anyone with the link',
		Public: 'Listed on Discover and your profile'
	};

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

	/**
	 * Where this playlist can be seen by somebody who is not its owner, once it is published.
	 *
	 * Null while it is private, because there is nothing to look at. The `preview` flag is what
	 * stops the public route bouncing the owner back here — opening your own public address
	 * normally means you want to edit, and that default is right; this is the exception.
	 */
	const publicPreviewHref = $derived.by(() => {
		if (!isOwner || visibility === 'Private') return null;

		const username = page.data.user?.username;
		return username
			? `/public/${encodeURIComponent(username)}/${encodeURIComponent(playlist.slug)}?preview=1`
			: null;
	});

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

	let forking = $state(false);
	let forkError = $state<string | null>(null);

	/**
	 * Takes a copy into your own library.
	 *
	 * Following is a stream of what this list gains next; forking is the thing you are looking
	 * at. They answer different questions, so they are different buttons.
	 */
	async function fork() {
		if (!isLoggedIn || !ownerUsername) return;

		forking = true;
		forkError = null;
		try {
			const res = await api.post(
				`/public/playlists/${encodeURIComponent(ownerUsername)}/${encodeURIComponent(playlist.slug)}/fork`
			);

			if (res.ok) {
				// Straight into the copy. Taking one and being left on the original is a
				// half-finished action — the point was to have it.
				const mine = (await res.json()) as Playlist;
				await goto(`/playlists/${mine.id}`);
			} else {
				forkError =
					res.status === 429
						? 'Too many at once. Wait a minute and try again.'
						: 'Could not take a copy of that.';
			}
		} finally {
			forking = false;
		}
	}

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
			} else {
				toast.error(failureMessage(res.status, followedByMe ? 'Could not unfollow this.' : 'Could not follow this.'));
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
			} else {
				toast.error(failureMessage(res.status, 'Could not change your like.'));
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

		const res = await api.patch(`/playlists/${playlist.id}`, { nsfw: next });
		if (res.ok) {
			isNsfw = ((await res.json()) as Playlist).nsfw;
		} else {
			nsfwSetting = previous;
			toast.error(failureMessage(res.status, 'Could not change the adult-content setting.'));
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
		if (res.ok) {
			playlistName = name;
			toast.success('Renamed.');
		} else {
			el.value = playlistName;
			toast.error(failureMessage(res.status, 'Could not rename the playlist.'));
		}
	}

	async function deletePlaylist() {
		// It goes to the trash, and the question used to say "This cannot be undone" — which was
		// not true, and made a recoverable step sound final.
		const ok = await confirmDialog(`Move "${playlistName}" to the trash?`, {
			description: 'You can restore it from the trash for 30 days.',
			danger: true,
			confirmLabel: 'Move to trash'
		});
		if (!ok) return;

		const id = playlist.id;
		const res = await api.del(`/playlists/${id}`);
		if (res.ok || res.status === 204) {
			await goto(resolvedBackHref);
			toast.success(`"${playlistName}" moved to the trash.`, {
				action: {
					label: 'Undo',
					run: async () => {
						const restored = await api.post(`/trash/playlists/${id}/restore`);
						if (restored.ok) await goto(`/playlists/${id}`);
						else toast.error(failureMessage(restored.status, 'Could not restore it. It is still in the trash.'));
					}
				}
			});
		} else {
			toast.error(failureMessage(res.status, 'Could not delete the playlist.'));
		}
	}

	async function setVisibility(next: Visibility) {
		const prev = visibility;
		visibility = next;
		const res = await api.patch(`/playlists/${playlist.id}`, { visibility: next });
		if (res.ok) toast.success(`${visConfig[next].label}: ${VIS_HINTS[next].toLowerCase()}.`);
		if (!res.ok) {
			visibility = prev;
			toast.error(failureMessage(res.status, 'Could not change who can see this.'));
		}
	}

	/**
	 * A link just added from the box at the top.
	 *
	 * It used to go on the end of whatever was loaded, which on a list sorted newest-first — or
	 * one long enough to have more pages — put it somewhere nobody was looking, with nothing to
	 * say it had landed. It goes where the current order puts it, is scrolled to, and is said.
	 */
	async function onAdded(item: PlaylistItem) {
		if (total !== null) total = total + 1;

		if (statusFilter === 'Watched') {
			toast.success('Added. It is not shown while the list is filtered to watched links.');
			return;
		}

		const newestFirst = serverSort === 'date-desc' || serverSort.startsWith('score') || serverSort === 'shuffle';
		items = newestFirst ? [item, ...items] : [...items, item];
		toast.success('Added.');

		await tick();
		document
			.querySelector(`[data-item-id="${item.id}"]`)
			?.scrollIntoView({ block: 'nearest', behavior: scrollBehavior() });
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

	/** A refetch in flight, so the list can say it is about to change rather than sit stale. */
	let refreshing = $state(false);

	/** Fetches the first page again for the current sort, filters and search. */
	async function fetchFirstPage() {
		refreshing = true;
		try {
			const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
			if (res.ok) applyPage((await res.json()) as Paged<PlaylistItem>);
			else toast.error(failureMessage(res.status, 'Could not load the links.'));
		} catch {
			toast.error('Could not reach the server.');
		} finally {
			refreshing = false;
		}
	}

	const reloadItems = fetchFirstPage;

	async function onfetchsort(sort: string) {
		serverSort = sort;
		savePrefs(playlist.id, { sort });
		nextCursor = null; // clear stale cursor immediately while loading
		await fetchFirstPage();
	}

	async function applySourceFilter(source: string | null) {
		sourceFilter = source;
		savePrefs(playlist.id, { source });
		nextCursor = null;
		await fetchFirstPage();
	}

	async function applyStatusFilter(status: StatusFilter) {
		statusFilter = status;
		savePrefs(playlist.id, { status });
		nextCursor = null;
		await fetchFirstPage();
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
	<BackLink href={resolvedBackHref} label={resolvedBackLabel} />

	<header class="mt-3 flex flex-wrap items-start justify-between gap-3">
		<div class="min-w-0 flex-1 basis-full sm:basis-auto">
			{#if isOwner}
				<!-- The owner sees this page more than anybody and had no h1 at all: the outline
				     began at "Sources" and the name announced as "edit text". The heading carries
				     the text and the input carries the editing, rather than one element failing
				     to be both — a heading that contains only a field has no accessible name. -->
				<h1 class="sr-only">{playlistName}</h1>
				<input
					type="text"
					value={playlistName}
					aria-label="Playlist name"
					title="Rename this playlist"
					class="-mx-1 w-full min-w-0 rounded-control border border-transparent bg-transparent px-1 py-0.5 text-2xl font-semibold hover:border-border-strong focus:border-accent"
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
		<div class="flex flex-wrap items-center gap-2">
			{#if isOwner}
				<Menu triggerClass={buttonClass('secondary', 'sm')} title="Change visibility" align="end">
					{#snippet trigger()}
						<currentVis.icon size={15} aria-hidden="true" />
						<span class="sr-only">Visibility:</span>
						{currentVis.label}
						<ChevronDown size={13} aria-hidden="true" />
					{/snippet}
					<MenuRadio
						value={visibility}
						options={(Object.entries(visConfig) as [Visibility, VisOption][]).map(([value, option]) => ({
							value,
							label: option.label,
							description: VIS_HINTS[value],
							icon: option.icon
						}))}
						onchange={setVisibility}
					/>
				</Menu>
				{#if publicPreviewHref}
					<!-- The obvious thing to want straight after publishing, and previously
					     impossible: your own public address redirected you back to this editor. -->
					<Button href={publicPreviewHref} size="sm" icon={Eye} title="See this the way a visitor does">
						<span class="hidden sm:inline">View as a visitor</span>
						<span class="sr-only sm:hidden">View as a visitor</span>
					</Button>
				{/if}
				{#if visibility !== 'Private' && (likeCount > 0 || followerCount > 0)}
					<!-- Read-only, and only once somebody has actually done it: a published
					     playlist showing "0 likes" says something discouraging and untrue about a
					     list nobody has found yet. The owner could not see either number before —
					     the API returned zero on their own reads — so the one person with a
					     reason to care was the one person who could not find out. -->
					<p class="inline-flex items-center gap-3 text-xs" style="color: var(--color-muted)">
						{#if likeCount > 0}
							<span class="inline-flex items-center gap-1">
								<Heart size={13} aria-hidden="true" />
								{likeCount}
								<span class="sr-only">{likeCount === 1 ? 'like' : 'likes'}</span>
							</span>
						{/if}
						{#if followerCount > 0}
							<span class="inline-flex items-center gap-1">
								<Rss size={13} aria-hidden="true" />
								{followerCount}
								<span class="sr-only">{followerCount === 1 ? 'follower' : 'followers'}</span>
							</span>
						{/if}
					</p>
				{/if}
			{/if}
			{#if !isOwner}
				<!-- The lightest thing a visitor can say about someone else's list, and the only
				     thing they do here that its owner ever sees. Shown to anonymous visitors as a
				     count they can read but not add to. -->
				<button
					type="button"
					onclick={toggleLike}
					disabled={!isLoggedIn || liking}
					class={buttonClass('secondary', 'sm', false, likedByMe ? 'border-accent text-accent' : '')}
					title={isLoggedIn
						? (likedByMe ? 'You like this' : 'Like this playlist')
						: 'Sign in to like this'}
					aria-pressed={likedByMe}
				>
					<Heart size={15} aria-hidden="true" fill={likedByMe ? 'currentColor' : 'none'} />
					{likeCount}
					<span class="sr-only">{likeCount === 1 ? 'like' : 'likes'}</span>
				</button>
			{/if}
			{#if !isOwner && isLoggedIn}
				<button
					type="button"
					onclick={toggleFollow}
					disabled={following}
					class={buttonClass('secondary', 'sm', false, followedByMe ? 'border-accent text-accent' : '')}
					aria-pressed={followedByMe}
					title={followedByMe ? 'New links here reach your feed' : 'Get new links from this list in your feed'}
				>
					<Rss size={15} aria-hidden="true" />
					{followedByMe ? 'Following' : 'Follow'}{followerCount ? ` · ${followerCount}` : ''}
				</button>
			{/if}
			{#if !isOwner && isLoggedIn && ownerUsername}
				<Button
					size="sm"
					icon={CopyPlus}
					onclick={fork}
					loading={forking}
					title="Copy these links into a playlist of your own"
				>
					{forking ? 'Copying…' : 'Take a copy'}{playlist.forkCount ? ` · ${playlist.forkCount}` : ''}
				</Button>
			{:else if isOwner && playlist.forkCount}
				<!-- The number worth more than the like count: somebody decided to keep this. -->
				<span
					class="inline-flex items-center gap-1.5 px-1 text-sm"
					style="color: var(--color-muted)"
					title="People who took a copy of this playlist"
				>
					<CopyPlus size={15} aria-hidden="true" /> {playlist.forkCount} copied
				</span>
			{/if}
			{#if forkError}
				<span class="text-xs" style="color: var(--color-danger)" role="alert">{forkError}</span>
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
				<Menu
					triggerClass={buttonClass('secondary', 'sm', false, isNsfw ? 'border-danger text-danger' : '')}
					title="Adult content"
					align="end"
					width="w-60"
				>
					{#snippet trigger()}
						{isNsfw ? 'Adult' : 'Not adult'}
						<ChevronDown size={13} aria-hidden="true" />
					{/snippet}
					<MenuRadio
						value={nsfwSetting}
						options={(['Auto', 'No', 'Yes'] as const).map((option) => ({
							value: option,
							label: NSFW_LABELS[option],
							description: option === 'Auto' ? 'Go by what the sites declare' : undefined
						}))}
						onchange={setNsfw}
					/>
				</Menu>
			{/if}
			{#if isOwner}
				<Menu
					triggerClass={buttonClass('ghost', 'md', true)}
					title="Export this playlist"
					label="Export this playlist"
					align="end"
				>
					{#snippet trigger()}
						<Download size={17} aria-hidden="true" />
					{/snippet}
					{#each PLAYLIST_EXPORTS as fmt (fmt.format)}
						<MenuItem icon={Download} href={`/api/v1/export/playlists/${playlist.id}?format=${fmt.format}`} download>
							{fmt.label}
						</MenuItem>
					{/each}
				</Menu>
				<Button variant="ghost-danger" icon={Trash2} iconOnly label="Delete playlist" onclick={deletePlaylist} />
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

	<!-- Dimmed and marked busy while a new sort, filter or search is on its way, so the list
	     being shown is never mistaken for the answer. -->
	<div class="mt-5 transition-opacity" class:opacity-60={refreshing} aria-busy={refreshing}>
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
			shared={role !== undefined}
			bind:total
		/>
		{#if nextCursor}
			<LoadMore onclick={loadMore} loading={loadingMore} remaining={total === null ? null : total - items.length} />
		{/if}
	</div>
</section>
