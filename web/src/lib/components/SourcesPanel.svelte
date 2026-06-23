<script lang="ts">
	import { Popover } from 'bits-ui';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Link, Play, Plus, Search, Unlink, UserPlus, X } from '@lucide/svelte';
	import SourceListItem from './SourceListItem.svelte';
	import PlaylistPickerDialog from './PlaylistPickerDialog.svelte';
	import type { AttachedSource, SharedSource, SourceSummary } from '$lib/types';

	let {
		playlistId,
		attached = $bindable(),
		ownSources = [],
		isOwner = true,
		isLoggedIn = false,
		fromHref,
		fromLabel,
		onreloaditems
	}: {
		playlistId: string;
		attached: AttachedSource[];
		ownSources?: SourceSummary[];
		isOwner?: boolean;
		isLoggedIn?: boolean;
		fromHref?: string;
		fromLabel?: string;
		onreloaditems?: () => Promise<void>;
	} = $props();

	function sourceHref(id: string) {
		const base = `/sources/${id}`;
		if (!fromHref) return base;
		return `${base}?from=${encodeURIComponent(fromHref)}&fromLabel=${encodeURIComponent(fromLabel ?? 'Back')}`;
	}

	let query = $state('');
	let sharedResults = $state<SharedSource[]>([]);
	let searching = $state(false);
	let searched = $state(false);
	let busy = $state(false);
	let addOpen = $state(false);

	// Subscribe-to-playlist (non-owner logged-in users)
	let subscribeSourceId = $state<string | null>(null);
	let subscribeSourceName = $state('');
	let subscribeOpen = $state(false);

	const attachedIds = $derived(new Set(attached.map((s) => s.id)));
	const ownUnattached = $derived(ownSources.filter((s) => !attachedIds.has(s.id)));
	const ownFiltered = $derived(
		query.trim()
			? ownUnattached.filter((s) => s.name.toLowerCase().includes(query.trim().toLowerCase()))
			: ownUnattached
	);

	function displayType(type: string) {
		return type === 'Rss' ? 'RSS' : type;
	}

	$effect(() => {
		if (!addOpen) {
			query = '';
			sharedResults = [];
			searched = false;
		}
	});

	async function refresh() {
		const res = await api.get(`/playlists/${playlistId}/sources`);
		if (res.ok) attached = (await res.json()) as AttachedSource[];
	}

	async function subscribe(sourceId: string) {
		busy = true;
		try {
			const res = await api.post(`/playlists/${playlistId}/sources`, { sourceId });
			if (res.ok) {
				await refresh();
				const { discoveredCount } = (await res.json()) as { discoveredCount: number };
				if (discoveredCount > 0) {
					const backfill = await confirmDialog(
						`This source has discovered ${discoveredCount} link${discoveredCount === 1 ? '' : 's'}. Add them all to this playlist?`,
						{ confirmLabel: 'Add all' }
					);
					if (backfill) {
						await api.post(`/playlists/${playlistId}/sources/${sourceId}/backfill`);
						await onreloaditems?.();
					}
				}
			}
		} finally {
			busy = false;
		}
	}

	async function run(sourceId: string) {
		await api.post(`/sources/${sourceId}/run`);
	}

	async function detach(sourceId: string) {
		busy = true;
		try {
			const res = await api.del(`/playlists/${playlistId}/sources/${sourceId}`);
			if (res.ok || res.status === 204) attached = attached.filter((s) => s.id !== sourceId);
		} finally {
			busy = false;
		}
	}

	async function searchShared() {
		if (!query.trim()) { sharedResults = []; searched = false; return; }
		searching = true;
		try {
			const res = await api.get(`/sources/shared?q=${encodeURIComponent(query.trim())}`);
			if (res.ok) sharedResults = ((await res.json()) as SharedSource[]).filter((s) => !attachedIds.has(s.id));
		} finally {
			searching = false;
			searched = true;
		}
	}

	function openSubscribeDialog(src: AttachedSource) {
		subscribeSourceId = src.id;
		subscribeSourceName = src.name;
		subscribeOpen = true;
	}

	async function subscribeToPlaylist(targetPlaylistId: string): Promise<string | null | undefined> {
		if (!subscribeSourceId) return;
		const sourceId = subscribeSourceId;
		const res = await api.post(`/playlists/${targetPlaylistId}/sources`, { sourceId });
		if (!res.ok) throw new Error();
		const { discoveredCount } = (await res.json()) as { discoveredCount: number };
		if (discoveredCount > 0) {
			const backfill = await confirmDialog(
				`This source has discovered ${discoveredCount} link${discoveredCount === 1 ? '' : 's'}. Add them all to this playlist?`,
				{ confirmLabel: 'Add all' }
			);
			if (backfill) await api.post(`/playlists/${targetPlaylistId}/sources/${sourceId}/backfill`);
		}
		return undefined; // 'Added'
	}
</script>

<div class="rounded-lg border px-4 py-3" style="border-color: var(--color-border); background: var(--color-surface)">
	<div class="flex items-center justify-between">
		<h2 class="font-medium">Sources</h2>
		{#if isOwner}
			<Popover.Root bind:open={addOpen}>
				<Popover.Trigger
					class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
					title={addOpen ? 'Cancel' : 'Add source'}
					aria-label={addOpen ? 'Cancel' : 'Add source'}
				>
					{#if addOpen}
						<X size={16} aria-hidden="true" />
					{:else}
						<Plus size={16} aria-hidden="true" />
					{/if}
				</Popover.Trigger>
				<Popover.Content
					class="popover-surface z-30 w-72 max-h-96 overflow-y-auto rounded-lg border p-3 shadow-md"
					align="end"
					sideOffset={6}
				>
					<div class="flex gap-2">
						<input
							bind:value={query}
							placeholder="Search sources..."
							aria-label="Search sources"
							class="flex-1 rounded border px-2 py-1 text-sm"
							style="border-color: var(--color-border); background: var(--color-bg)"
							onkeydown={(e) => e.key === 'Enter' && searchShared()}
						/>
						<button type="button" onclick={searchShared} disabled={searching} class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60" title="Search shared" aria-label="Search shared sources">
							<Search size={16} aria-hidden="true" />
						</button>
					</div>

					{#if ownFiltered.length}
						<div class="mt-3">
							<div class="mb-1.5 text-xs font-medium" style="color: var(--color-muted)">Yours</div>
							<ul class="flex flex-col gap-1.5">
								{#each ownFiltered as src (src.id)}
									<SourceListItem name={src.name} badge={displayType(src.type)} href={sourceHref(src.id)}>
										{#snippet actions()}
											<button type="button" onclick={() => subscribe(src.id)} disabled={busy} title="Attach source" aria-label="Attach source" class="inline-flex items-center rounded p-0.5 hover:opacity-70" style="color: var(--color-accent)">
												<Link size={15} aria-hidden="true" />
											</button>
										{/snippet}
									</SourceListItem>
								{/each}
							</ul>
						</div>
					{/if}

					{#if sharedResults.length}
						<div class="mt-3">
							<div class="mb-1.5 text-xs font-medium" style="color: var(--color-muted)">Shared</div>
							<ul class="flex flex-col gap-1.5">
								{#each sharedResults as src (src.id)}
									<SourceListItem name={src.name} badge={displayType(src.type)} subtitle={`@${src.ownerUsername}`}>
										{#snippet actions()}
											<button type="button" onclick={() => subscribe(src.id)} disabled={busy} title="Subscribe" aria-label="Subscribe to source" class="inline-flex items-center rounded p-0.5 hover:opacity-70" style="color: var(--color-accent)">
												<UserPlus size={16} aria-hidden="true" />
											</button>
										{/snippet}
									</SourceListItem>
								{/each}
							</ul>
						</div>
					{:else if searched && !searching}
						<p class="mt-2 text-xs" style="color: var(--color-muted)">No shared sources found.</p>
					{/if}
				</Popover.Content>
			</Popover.Root>
		{/if}
	</div>

	{#if attached.length}
		<ul class="mt-2 flex flex-col gap-2">
			{#each attached as src (src.id)}
				<SourceListItem
					name={src.name}
					badge={displayType(src.type)}
					href={src.ownedByMe ? sourceHref(src.id) : undefined}
					subtitle={src.ownedByMe ? undefined : `@${src.ownerUsername}`}
				>
					{#snippet actions()}
						{#if isOwner}
							{#if src.ownedByMe}
								<button type="button" onclick={() => run(src.id)} disabled={busy} title="Run now" aria-label="Run now" class="inline-flex items-center rounded p-0.5 hover:opacity-70">
									<Play size={15} aria-hidden="true" />
								</button>
							{/if}
							<button type="button" onclick={() => detach(src.id)} disabled={busy} title="Unsubscribe" aria-label="Unsubscribe" class="inline-flex items-center rounded p-0.5 hover:opacity-70" style="color: var(--color-danger)">
								<Unlink size={15} aria-hidden="true" />
							</button>
						{:else if isLoggedIn}
							<button
								type="button"
								onclick={() => openSubscribeDialog(src)}
								title="Add to my playlist"
								aria-label="Add to my playlist"
								class="inline-flex items-center rounded p-0.5 hover:opacity-70"
								style="color: var(--color-accent)"
							>
								<UserPlus size={15} aria-hidden="true" />
							</button>
						{/if}
					{/snippet}
				</SourceListItem>
			{/each}
		</ul>
	{:else}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			{isOwner ? 'No sources attached.' : 'No shared sources.'}
		</p>
	{/if}
</div>

{#if !isOwner && isLoggedIn}
	<PlaylistPickerDialog
		bind:open={subscribeOpen}
		title="Add source to playlist"
		subtitle={subscribeSourceName}
		onselect={subscribeToPlaylist}
	/>
{/if}
