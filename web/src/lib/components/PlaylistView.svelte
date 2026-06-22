<script lang="ts">
	import AddLinkBar from './AddLinkBar.svelte';
	import LinkTable from './LinkTable.svelte';
	import TagEditor from './TagEditor.svelte';
	import SourcesPanel from './SourcesPanel.svelte';
	import SaveToFolderDialog from './SaveToFolderDialog.svelte';
	import { Popover } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { savePrefs } from '$lib/prefs';
	import { ChevronDown, EyeOff, Globe, Lock } from '@lucide/svelte';
	import type { AttachedSource, Paged, Playlist, PlaylistItem, SourceSummary, Visibility } from '$lib/types';
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
		isLoggedIn?: boolean;
		ownerUsername?: string;
		backHref?: string;
		backLabel?: string;
		initialPrefs?: PlaylistPrefs;
	} = $props();

	const resolvedBackHref = $derived(backHref ?? (isOwner ? '/' : '/discover'));
	const resolvedBackLabel = $derived(backLabel ?? (isOwner ? 'Playlists' : 'Discover'));

	let items = $state(itemsPage.items);
	let nextCursor = $state(itemsPage.nextCursor);
	let serverSort = $state(initialPrefs?.sort ?? 'position');
	let sourceFilter = $state<string | null>(initialPrefs?.source ?? null);
	let tags = $state(playlist.tags);
	let attached = $state(attachedSources);
	let loadingMore = $state(false);
	let visibility = $state(playlist.visibility);
	let visOpen = $state(false);
	const currentVis = $derived(visConfig[visibility] ?? visConfig.Private);

	async function setVisibility(next: Visibility) {
		const prev = visibility;
		visibility = next;
		const res = await api.patch(`/playlists/${playlist.id}`, { visibility: next });
		if (!res.ok) visibility = prev;
	}

	function onAdded(item: PlaylistItem) {
		items = [...items, item];
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
		const qs = p.toString();
		return qs ? `?${qs}` : '';
	}

	async function reloadItems() {
		const res = await api.get(`${itemsEndpoint()}?sort=${serverSort}`);
		if (res.ok) {
			const page = (await res.json()) as Paged<PlaylistItem>;
			items = page.items;
			nextCursor = page.nextCursor;
		}
	}

	async function onfetchsort(sort: string) {
		serverSort = sort;
		savePrefs(playlist.id, { sort });
		const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
		if (res.ok) {
			const page = (await res.json()) as Paged<PlaylistItem>;
			items = page.items;
			nextCursor = page.nextCursor;
		}
	}

	async function applySourceFilter(source: string | null) {
		sourceFilter = source;
		savePrefs(playlist.id, { source });
		const res = await api.get(`${itemsEndpoint()}${buildParams()}`);
		if (res.ok) {
			const page = (await res.json()) as Paged<PlaylistItem>;
			items = page.items;
			nextCursor = page.nextCursor;
		}
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
			<h1 class="text-2xl font-semibold">{playlist.name}</h1>
			{#if ownerUsername}
				<p class="mt-0.5 text-sm" style="color: var(--color-muted)">by @{ownerUsername}</p>
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
			{#if isLoggedIn}
				<SaveToFolderDialog
					playlistId={playlist.id}
					currentFolderId={playlist.folderId}
					currentFolderName={playlist.folderName}
				/>
			{/if}
		</div>
	</header>

	<div class="mt-3">
		<TagEditor playlistId={playlist.id} bind:tags readonly={!isOwner} />
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

	{#if isOwner}
		<div class="mt-5">
			<AddLinkBar playlistId={playlist.id} {onAdded} />
		</div>
	{/if}

	<div class="mt-5">
		<LinkTable
			bind:items
			readonly={!isOwner}
			{onfetchsort}
			playlistId={playlist.id}
			{initialPrefs}
			attachedSources={attached}
			{sourceFilter}
			onsourcefilter={applySourceFilter}
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
