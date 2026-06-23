<script lang="ts">
	import { Popover } from 'bits-ui';
	import { dndzone } from 'svelte-dnd-action';
	import { api } from '$lib/api/client';
	import { ArrowDown, ArrowUp, ArrowUpDown, Check, ChevronDown, Clock, Eye, EyeOff, Image, MoreVertical, Rss, Share2, Star, StickyNote, Trash2, Type, X } from '@lucide/svelte';
	import PlaylistPickerDialog from './PlaylistPickerDialog.svelte';
	import NsfwBadge from './NsfwBadge.svelte';
	import { savePrefs } from '$lib/prefs';
	import type { AttachedSource, PlaylistItem } from '$lib/types';
	import type { PlaylistPrefs } from '$lib/prefs';

	let {
		items = $bindable(),
		readonly = false,
		onfetchsort,
		onmove,
		playlistId = undefined,
		initialPrefs = undefined,
		attachedSources = [],
		sourceFilter = null,
		onsourcefilter
	}: {
		items: PlaylistItem[];
		readonly?: boolean;
		onfetchsort?: (sort: string) => Promise<void>;
		onmove?: () => Promise<void>;
		playlistId?: string;
		initialPrefs?: PlaylistPrefs;
		attachedSources?: AttachedSource[];
		sourceFilter?: string | null;
		onsourcefilter?: (source: string | null) => Promise<void>;
	} = $props();

	type SortMode = 'manual' | 'date-asc' | 'date-desc' | 'shuffle' | 'score-asc' | 'score-desc';
	type StatusFilter = 'All' | 'Unwatched' | 'Watched';

	function serverSortToMode(s: string | undefined): SortMode {
		if (s === 'date-asc') return 'date-asc';
		if (s === 'date-desc') return 'date-desc';
		if (s === 'shuffle') return 'shuffle';
		if (s === 'score-asc') return 'score-asc';
		if (s === 'score-desc') return 'score-desc';
		return readonly ? 'date-desc' : 'manual';
	}

	const defaultStatus: StatusFilter = !readonly ? 'Unwatched' : 'All';

	let sortMode = $state<SortMode>(serverSortToMode(initialPrefs?.sort));
	let statusFilter = $state<StatusFilter>((initialPrefs?.status as StatusFilter | null) ?? defaultStatus);
	let showThumbnails = $state(initialPrefs?.showThumbnails ?? true);
	let showUrls = $state(initialPrefs?.showUrls ?? false);
	let forceShowScore = $state(false);

	const statusOptions: StatusFilter[] = ['All', 'Unwatched', 'Watched'];
	const SORT_LABELS: Record<SortMode, string> = {
		manual: 'Manual',
		'date-asc': 'Oldest',
		'date-desc': 'Newest',
		shuffle: 'Shuffle',
		'score-asc': 'Score ↑',
		'score-desc': 'Score ↓'
	};

	let sourceFilterOpen = $state(false);
	let statusOpen = $state(false);
	let sortOpen = $state(false);
	let displayOpen = $state(false);

	const sourceFilterLabel = $derived(
		sourceFilter === null
			? 'All'
			: sourceFilter === 'manual'
				? 'Manual'
				: (attachedSources.find((s) => s.id === sourceFilter)?.name ?? 'Unknown')
	);

	// Score column: show when any loaded item has a score, user forced it, or current sort is score-based
	const hasAnyScore = $derived(items.some((i) => i.score !== null));
	const showScoreCol = $derived(!readonly && (hasAnyScore || forceShowScore || sortMode === 'score-asc' || sortMode === 'score-desc'));

	function setStatusFilter(f: StatusFilter) {
		statusFilter = f;
		if (playlistId) savePrefs(playlistId, { status: f });
	}

	// Items visible under the current status filter
	const filteredItems = $derived(
		statusFilter === 'All'
			? items
			: statusFilter === 'Unwatched'
				? items.filter((i) => i.status === 'Added')
				: items.filter((i) => i.status === 'Watched')
	);

	// DND state — syncs from filteredItems when items change externally
	let dndItems = $state<PlaylistItem[]>([]);
	$effect(() => {
		dndItems = filteredItems;
	});

	function setSort(mode: SortMode) {
		sortMode = mode;
		const serverSort =
			mode === 'date-asc' ? 'date-asc'
			: mode === 'date-desc' ? 'date-desc'
			: mode === 'shuffle' ? 'shuffle'
			: mode === 'score-asc' ? 'score-asc'
			: mode === 'score-desc' ? 'score-desc'
			: 'position';
		onfetchsort?.(serverSort);
	}

	function clickAddedHeader() {
		if (sortMode === 'date-desc') setSort('date-asc');
		else if (sortMode === 'date-asc') setSort(readonly ? 'date-desc' : 'manual');
		else setSort('date-desc');
	}

	function clickScoreHeader() {
		if (sortMode === 'score-desc') setSort('score-asc');
		else if (sortMode === 'score-asc') setSort(readonly ? 'score-desc' : 'manual');
		else setSort('score-desc');
	}

	function sortByDate(arr: PlaylistItem[], asc: boolean) {
		return [...arr].sort((a, b) => {
			const diff = new Date(a.creationTime).getTime() - new Date(b.creationTime).getTime();
			return asc ? diff : -diff;
		});
	}

	const displayItems = $derived.by(() => {
		if (sortMode === 'date-asc') return sortByDate(filteredItems, true);
		if (sortMode === 'date-desc') return sortByDate(filteredItems, false);
		return filteredItems; // manual, shuffle, score: server order is canonical
	});

	let noteEditId = $state<string | null>(null);
	let draftNote = $state('');
	let actionFlyoutId = $state<string | null>(null);
	let shareItem = $state<PlaylistItem | null>(null);
	let shareOpen = $state(false);

	const FLIP = 150;
	const useDnd = $derived(!readonly && sortMode === 'manual');

	function onConsider(e: CustomEvent<{ items: PlaylistItem[] }>) {
		dndItems = e.detail.items;
	}

	async function onFinalize(e: CustomEvent<{ items: PlaylistItem[]; info: { id: string } }>) {
		dndItems = e.detail.items;
		const movedId = e.detail.info.id;
		const idx = dndItems.findIndex((i) => i.id === movedId);
		const afterItemId = idx > 0 ? dndItems[idx - 1].id : null;
		// Merge reordered filtered items back; items not in the current filter stay in place
		const filteredIds = new Set(filteredItems.map((i) => i.id));
		items = [...items.filter((i) => !filteredIds.has(i.id)), ...dndItems];
		await api.post(`/items/${movedId}/move`, { afterItemId });
		await onmove?.();
	}

	async function saveNote(item: PlaylistItem) {
		const note = draftNote.trim() || null;
		const res = await api.patch(`/items/${item.id}`, { note });
		if (res.ok) items = items.map((i) => (i.id === item.id ? { ...i, note } : i));
	}

	// Returns the saved score (or undefined if nothing changed / invalid).
	async function saveScore(item: PlaylistItem, rawValue: string): Promise<number | null | undefined> {
		const score = rawValue === '' ? null : Math.max(0, Math.min(100, parseInt(rawValue, 10)));
		if (score !== null && isNaN(score)) return undefined;
		if (score === item.score) return item.score;
		const res = await api.put(`/items/${item.id}/score`, { score });
		if (res.ok) items = items.map((i) => (i.id === item.id ? { ...i, score } : i));
		return score;
	}

	async function remove(item: PlaylistItem) {
		const res = await api.del(`/items/${item.id}`);
		if (res.ok || res.status === 204) items = items.filter((i) => i.id !== item.id);
	}

	async function toggleWatched(item: PlaylistItem) {
		const newStatus = item.status === 'Watched' ? 'Added' : 'Watched';
		const res = await api.patch(`/items/${item.id}`, { status: newStatus });
		if (res.ok) items = items.map((i) => (i.id === item.id ? { ...i, status: newStatus } : i));
	}

	function dateAdded(iso: string) {
		return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
	}

	function isPending(item: PlaylistItem) {
		return !item.link.enriched;
	}

	const toggleClass = 'rounded-full border px-2.5 py-0.5 text-xs transition-colors';
	function toggleStyle(active: boolean) {
		return active
			? 'border-color: var(--color-accent); color: var(--color-accent)'
			: 'border-color: var(--color-border); color: var(--color-muted)';
	}
</script>

{#snippet row(item: PlaylistItem, draggable: boolean)}
	<tr
		class="border-t align-middle"
		style="border-color: var(--color-border); {item.status === 'Watched' ? 'opacity: 0.45' : ''}"
	>
		{#if !readonly}
			<td
				class="select-none pr-1"
				class:cursor-grab={draggable}
				style="color: var(--color-muted)"
				title={draggable ? 'Drag to reorder' : undefined}
			>
				{#if draggable}<span role="img" aria-label="Drag to reorder">⋮⋮</span>{/if}
			</td>
		{/if}
		<td class="py-2 pr-3">
			<div class="flex items-center gap-4">
				{#if showThumbnails}
					{@const thumb = item.metadata?.thumbnail ?? item.link.thumbnailUrl}
					{#if thumb}
						<img
							src={thumb}
							alt=""
							class="shrink-0 rounded object-cover"
							style="height: 5em; width: auto"
						/>
					{/if}
				{/if}
				<div class="min-w-0">
					<a
						href={item.link.url}
						target="_blank"
						rel="noopener noreferrer"
						class="break-words hover:underline"
					>
						{showUrls ? item.link.url : (item.metadata?.title ?? item.link.title ?? item.link.url)}
					</a>
					{#if item.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}
					{#if item.metadata?.author}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.metadata.author}</p>
					{/if}
					{#if item.note && readonly}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.note}</p>
					{/if}
				</div>
			</div>
		</td>
		<td class="whitespace-nowrap text-center" style="color: var(--color-muted)">{dateAdded(item.creationTime)}</td>
		{#if showScoreCol}
			<td class="w-10 pl-6 text-center" style="color: var(--color-muted)">
				<input
					type="number"
					min="0"
					max="100"
					value={item.score ?? ''}
					placeholder="—"
					class="w-10 border-none bg-transparent text-center text-sm [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none focus-visible:!outline-none"
					style="color: inherit"
					oninput={(e) => {
						const v = e.currentTarget.value;
						if (v !== '' && v !== '-') {
							const n = parseInt(v, 10);
							if (!isNaN(n)) e.currentTarget.value = String(Math.max(0, Math.min(100, n)));
						}
					}}
					onblur={async (e) => {
						const el = e.currentTarget;
						const saved = await saveScore(item, el.value);
						// Snap display to what was actually saved (handles clamped values)
						if (saved !== undefined) el.value = saved !== null ? String(saved) : '';
					}}
					onkeydown={(e) => { if (e.key === 'Enter') e.currentTarget.blur(); }}
				/>
			</td>
		{/if}
		{#if !readonly}
			<td class="w-8 text-right">
				<Popover.Root
					open={actionFlyoutId === item.id}
					onOpenChange={(o) => { actionFlyoutId = o ? item.id : (actionFlyoutId === item.id ? null : actionFlyoutId); }}
				>
					<Popover.Trigger
						class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
						style="color: var(--color-muted)"
						title="Actions"
						aria-label="Actions"
					>
						<MoreVertical size={16} aria-hidden="true" />
					</Popover.Trigger>
					<Popover.Content
						class="popover-surface z-30 rounded-lg border shadow-md"
						side="top"
						sideOffset={4}
					>
						<div class="flex items-center gap-0.5 px-1.5 py-1.5">
							<button
								type="button"
								onclick={() => { toggleWatched(item); actionFlyoutId = null; }}
								class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
								style="color: var(--color-muted)"
								title={item.status === 'Watched' ? 'Mark as unwatched' : 'Mark as watched'}
								aria-label={item.status === 'Watched' ? 'Mark as unwatched' : 'Mark as watched'}
							>
								{#if item.status === 'Watched'}
									<EyeOff size={16} aria-hidden="true" />
								{:else}
									<Eye size={16} aria-hidden="true" />
								{/if}
							</button>
							<button
								type="button"
								onclick={() => { actionFlyoutId = null; shareItem = item; shareOpen = true; }}
								class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
								style="color: var(--color-muted)"
								title="Add to another playlist"
								aria-label="Add to another playlist"
							>
								<Share2 size={16} aria-hidden="true" />
							</button>
							{#if !showScoreCol}
								<button
									type="button"
									onclick={() => { forceShowScore = true; actionFlyoutId = null; }}
									class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
									style="color: var(--color-muted)"
									title="Show score column"
									aria-label="Show score column"
								>
									<Star size={16} aria-hidden="true" />
								</button>
							{/if}
							<button
								type="button"
								onclick={() => {
									actionFlyoutId = null;
									if (noteEditId === item.id) { noteEditId = null; }
									else { noteEditId = item.id; draftNote = item.note ?? ''; }
								}}
								class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
								style={isPending(item) ? 'color: var(--color-muted)' : item.note ? 'color: var(--color-accent)' : 'color: var(--color-muted)'}
								title={item.note ? 'Edit note' : 'Add note'}
								aria-label={item.note ? 'Edit note' : 'Add note'}
							>
								{#if isPending(item)}
									<Clock size={16} aria-hidden="true" />
								{:else}
									<StickyNote size={16} aria-hidden="true" />
								{/if}
							</button>
							<button
								type="button"
								onclick={() => { remove(item); actionFlyoutId = null; }}
								class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
								style="color: var(--color-danger)"
								title="Remove link"
								aria-label="Remove link"
							>
								<Trash2 size={16} aria-hidden="true" />
							</button>
						</div>
					</Popover.Content>
				</Popover.Root>
			</td>
		{/if}
	</tr>
	{#if !readonly && noteEditId === item.id}
		{@const colCount = 4 + (showScoreCol ? 1 : 0)}
		<tr class="border-t" style="border-color: var(--color-border)">
			<td colspan={colCount} class="px-2 py-2">
				<div class="flex items-start gap-2">
					<textarea
						bind:value={draftNote}
						placeholder="Add note…"
						rows={3}
						class="flex-1 resize-none rounded border px-2 py-1 text-sm"
						style="border-color: var(--color-border); background: var(--color-bg)"
						onkeydown={(e) => {
							if (e.key === 'Enter' && e.ctrlKey) { saveNote(item); noteEditId = null; }
							if (e.key === 'Escape') { noteEditId = null; }
						}}
					></textarea>
					<div class="flex flex-col gap-1">
						<button
							type="button"
							onclick={() => { saveNote(item); noteEditId = null; }}
							class="inline-flex items-center rounded p-1.5"
							style="background: var(--color-accent); color: var(--color-accent-contrast)"
							title="Save note"
							aria-label="Save note"
						>
							<Check size={14} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => { noteEditId = null; }}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							style="color: var(--color-muted)"
							title="Cancel"
							aria-label="Cancel"
						>
							<X size={14} aria-hidden="true" />
						</button>
					</div>
				</div>
			</td>
		</tr>
	{/if}
{/snippet}

{#if items.length > 0 || sourceFilter !== null}
	<div class="mb-3 flex flex-wrap items-center gap-1.5">
		<!-- First section: source + status filters -->
		{#if attachedSources.length > 0}
			<Popover.Root bind:open={sourceFilterOpen}>
				<Popover.Trigger
					class="{toggleClass} inline-flex items-center gap-1"
					style={toggleStyle(sourceFilter !== null)}
				>
					<Rss size={10} aria-hidden="true" /> {sourceFilterLabel} <ChevronDown size={10} aria-hidden="true" />
				</Popover.Trigger>
				<Popover.Content
					class="popover-surface z-30 min-w-28 overflow-hidden rounded-md border shadow-md"
					sideOffset={4}
				>
					<button
						type="button"
						onclick={() => { onsourcefilter?.(null); sourceFilterOpen = false; }}
						class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={sourceFilter === null}
					>All</button>
					<button
						type="button"
						onclick={() => { onsourcefilter?.('manual'); sourceFilterOpen = false; }}
						class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={sourceFilter === 'manual'}
					>Manual</button>
					{#each attachedSources as source (source.id)}
						<button
							type="button"
							onclick={() => { onsourcefilter?.(source.id); sourceFilterOpen = false; }}
							class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
							class:font-medium={sourceFilter === source.id}
						>{source.name}</button>
					{/each}
				</Popover.Content>
			</Popover.Root>
		{/if}
		{#if !readonly}
			<Popover.Root bind:open={statusOpen}>
				<Popover.Trigger
					class="{toggleClass} inline-flex items-center gap-1"
					style={toggleStyle(statusFilter !== 'Unwatched')}
				>
					<Eye size={10} aria-hidden="true" /> {statusFilter} <ChevronDown size={10} aria-hidden="true" />
				</Popover.Trigger>
				<Popover.Content
					class="popover-surface z-30 min-w-28 overflow-hidden rounded-md border shadow-md"
					sideOffset={4}
				>
					{#each statusOptions as f (f)}
						<button
							type="button"
							onclick={() => { setStatusFilter(f); statusOpen = false; }}
							class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
							class:font-medium={statusFilter === f}
						>{f}</button>
					{/each}
				</Popover.Content>
			</Popover.Root>
		{/if}
		{#if attachedSources.length > 0 || !readonly}
			<span class="text-xs" style="color: var(--color-border)">|</span>
		{/if}

		<!-- Second section: sort + display options -->
		<Popover.Root bind:open={sortOpen}>
			<Popover.Trigger
				class="{toggleClass} inline-flex items-center gap-1"
				style={toggleStyle(sortMode !== (readonly ? 'date-desc' : 'manual'))}
			>
				<ArrowUpDown size={10} aria-hidden="true" /> {SORT_LABELS[sortMode]} <ChevronDown size={10} aria-hidden="true" />
			</Popover.Trigger>
			<Popover.Content
				class="popover-surface z-30 min-w-28 overflow-hidden rounded-md border shadow-md"
				sideOffset={4}
			>
				{#if !readonly}
					<button
						type="button"
						onclick={() => { setSort('manual'); sortOpen = false; }}
						class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={sortMode === 'manual'}
					>Manual</button>
				{/if}
				<button
					type="button"
					onclick={() => { setSort('date-asc'); sortOpen = false; }}
					class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={sortMode === 'date-asc'}
				>Oldest</button>
				<button
					type="button"
					onclick={() => { setSort('date-desc'); sortOpen = false; }}
					class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={sortMode === 'date-desc'}
				>Newest</button>
				<button
					type="button"
					onclick={() => { setSort('shuffle'); sortOpen = false; }}
					class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={sortMode === 'shuffle'}
				>Shuffle</button>
				{#if showScoreCol}
					<button
						type="button"
						onclick={() => { setSort('score-desc'); sortOpen = false; }}
						class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={sortMode === 'score-desc'}
					>Score ↓</button>
					<button
						type="button"
						onclick={() => { setSort('score-asc'); sortOpen = false; }}
						class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={sortMode === 'score-asc'}
					>Score ↑</button>
				{/if}
			</Popover.Content>
		</Popover.Root>

		<span class="text-xs" style="color: var(--color-border)">|</span>

		<Popover.Root bind:open={displayOpen}>
			<Popover.Trigger
				class="{toggleClass} inline-flex items-center gap-1"
				style={toggleStyle(showUrls)}
			>
				<Type size={10} aria-hidden="true" /> {showUrls ? 'URL' : 'Title'} <ChevronDown size={10} aria-hidden="true" />
			</Popover.Trigger>
			<Popover.Content
				class="popover-surface z-30 min-w-24 overflow-hidden rounded-md border shadow-md"
				sideOffset={4}
			>
				<button
					type="button"
					onclick={() => { showUrls = false; displayOpen = false; if (playlistId) savePrefs(playlistId, { showUrls: false }); }}
					class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={!showUrls}
				>Title</button>
				<button
					type="button"
					onclick={() => { showUrls = true; displayOpen = false; if (playlistId) savePrefs(playlistId, { showUrls: true }); }}
					class="flex w-full items-center px-3 py-1.5 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={showUrls}
				>URL</button>
			</Popover.Content>
		</Popover.Root>

		<button
			type="button"
			onclick={() => { showThumbnails = !showThumbnails; if (playlistId) savePrefs(playlistId, { showThumbnails }); }}
			class="{toggleClass} inline-flex items-center gap-1"
			style={toggleStyle(showThumbnails)}
		>
			<Image size={11} aria-hidden="true" /> Thumbnail
		</button>
	</div>
{/if}

{#if items.length === 0}
	{#if sourceFilter !== null}
		<p class="py-6 text-center text-sm" style="color: var(--color-muted)">No items for this source.</p>
	{:else}
		<div
			class="rounded-lg border border-dashed p-10 text-center"
			style="border-color: var(--color-border)"
		>
			<p class="font-medium">No links yet.</p>
			{#if !readonly}
				<p class="mt-1 text-sm" style="color: var(--color-muted)">Paste a URL above to add the first.</p>
			{/if}
		</div>
	{/if}
{:else if filteredItems.length === 0}
	<p class="py-6 text-center text-sm" style="color: var(--color-muted)">
		No {statusFilter.toLowerCase()} items.
	</p>
{:else}
	<div class="overflow-x-auto">
			<table class="w-full border-collapse text-sm">
				<thead>
					<tr class="text-left" style="color: var(--color-muted)">
						{#if !readonly}<th class="w-6"></th>{/if}
						<th class="py-2 font-medium">{showUrls ? 'URL' : 'Title'}</th>
						<th class="py-2 text-center font-medium">
						<button
							type="button"
							onclick={clickAddedHeader}
							class="inline-flex items-center gap-1 hover:opacity-70"
							title="Sort by date added"
							aria-label="Sort by date added"
						>
							Added
							{#if sortMode === 'date-desc'}
								<ArrowDown size={12} aria-hidden="true" />
							{:else if sortMode === 'date-asc'}
								<ArrowUp size={12} aria-hidden="true" />
							{:else}
								<ArrowUpDown size={12} aria-hidden="true" style="opacity: 0.4" />
							{/if}
						</button>
					</th>
						{#if showScoreCol}
							<th class="w-16 py-2 pl-6 text-center font-medium">
								<button
									type="button"
									onclick={clickScoreHeader}
									class="inline-flex items-center gap-1 hover:opacity-70"
									title="Sort by score"
									aria-label="Sort by score"
								>
									Score
									{#if sortMode === 'score-desc'}
										<ArrowDown size={12} aria-hidden="true" />
									{:else if sortMode === 'score-asc'}
										<ArrowUp size={12} aria-hidden="true" />
									{:else}
										<ArrowUpDown size={12} aria-hidden="true" style="opacity: 0.4" />
									{/if}
								</button>
							</th>
						{/if}
						{#if !readonly}<th class="w-8"></th>{/if}
					</tr>
				</thead>
				{#if useDnd}
					<tbody
						use:dndzone={{ items: dndItems, flipDurationMs: FLIP, dropTargetStyle: {} }}
						onconsider={onConsider}
						onfinalize={onFinalize}
					>
						{#each dndItems as item (item.id)}
							{@render row(item, true)}
						{/each}
					</tbody>
				{:else}
					<tbody>
						{#each displayItems as item (item.id)}
							{@render row(item, false)}
						{/each}
					</tbody>
				{/if}
			</table>
		</div>
{/if}

{#if !readonly && playlistId}
	<PlaylistPickerDialog
		bind:open={shareOpen}
		title="Add to playlist"
		excludePlaylistId={playlistId}
		onselect={async (targetId) => {
			if (!shareItem) return;
			const res = await api.post(`/playlists/${targetId}/items`, { url: shareItem.link.url });
			if (res.ok) return undefined;
			if (res.status === 409) return 'Already here';
			throw new Error();
		}}
	/>
{/if}
