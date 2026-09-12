<script lang="ts">
	import { Popover } from 'bits-ui';
	import { dndzone } from 'svelte-dnd-action';
	import { SvelteSet } from 'svelte/reactivity';
	import { api } from '$lib/api/client';
	import { readingLabel } from '$lib/reading';
	import { AlertCircle, Archive, ArrowDown, ArrowUp, ArrowUpDown, BookOpen, Check, Link2, ChevronDown, Clock, Eye, EyeOff, Image, LayoutGrid, MoreVertical, Rows3, Rss, Share2, Star, StickyNote, Trash2, Type, X } from '@lucide/svelte';
	import PlaylistPickerDialog from './PlaylistPickerDialog.svelte';
	import NsfwBadge from './NsfwBadge.svelte';
	import KindBadge from './KindBadge.svelte';
	import { savePrefs } from '$lib/prefs';
	import { isPlainKey, moveFocus } from '$lib/keyboard';
	import {
		SORT_LABELS,
		canReorder,
		modeToServerSort,
		nextDateSort,
		nextScoreSort,
		orderForDisplay,
		serverSortToMode,
		type SortMode
	} from '$lib/sorting';
	import { confirmDialog } from '$lib/dialog.svelte';
	import type { AttachedSource, PlaylistItem } from '$lib/types';
	import type { PlaylistPrefs } from '$lib/prefs';

	type StatusFilter = 'All' | 'Unwatched' | 'Watched';

	let {
		items = $bindable(),
		readonly = false,
		onfetchsort,
		onmove,
		playlistId = undefined,
		initialPrefs = undefined,
		attachedSources = [],
		sourceFilter = null,
		onsourcefilter,
		statusFilter = 'All',
		onstatusfilter,
		isSearching = false,
		total = $bindable(null)
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
		statusFilter?: StatusFilter;
		onstatusfilter?: (status: StatusFilter) => Promise<void>;
		isSearching?: boolean;
		total?: number | null;
	} = $props();

	let sortMode = $state<SortMode>(serverSortToMode(initialPrefs?.sort, readonly));
	let showThumbnails = $state(initialPrefs?.showThumbnails ?? true);
	let viewMode = $state(initialPrefs?.viewMode === 'grid' ? 'grid' : 'table');
	let showUrls = $state(initialPrefs?.showUrls ?? false);
	let forceShowScore = $state(false);

	const statusOptions: StatusFilter[] = ['All', 'Unwatched', 'Watched'];
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

	// Multi-select. Every action below already exists per item; the point is doing it to a
	// selection without repeating yourself forty times.
	let selected = $state(new SvelteSet<string>());
	let bulkBusy = $state(false);
	let moveOpen = $state(false);
	let copyOpen = $state(false);

	const allSelected = $derived(items.length > 0 && selected.size === items.length);

	function toggleSelected(id: string) {
		if (!selected.delete(id)) selected.add(id);
	}

	function toggleSelectAll() {
		if (allSelected) selected.clear();
		else for (const item of items) selected.add(item.id);
	}

	async function bulk(body: Record<string, unknown>) {
		if (selected.size === 0) return;
		bulkBusy = true;
		try {
			const res = await api.post('/items/bulk', { itemIds: [...selected], ...body });
			if (res.ok) {
				selected.clear();
				await onmove?.();
			}
		} finally {
			bulkBusy = false;
		}
	}

	async function bulkDelete() {
		const count = selected.size;
		const ok = await confirmDialog(
			`Delete ${count} ${count === 1 ? 'link' : 'links'}? You can put them back from the trash.`,
			{ danger: true, confirmLabel: 'Delete' }
		);
		if (ok) await bulk({ action: 'Delete' });
	}

	// Links being re-fetched right now, so the button can say so rather than looking inert.
	let rechecking = $state(new SvelteSet<string>());

	async function recheck(item: PlaylistItem) {
		rechecking.add(item.link.id);
		const res = await api.post(`/links/${item.link.id}/recheck`);
		if (!res.ok) {
			rechecking.delete(item.link.id);
		}
		// On success the row stays marked until the page is reloaded: enrichment is asynchronous,
		// so there is nothing truthful to show yet.
	}

	// DND state — syncs from items when they change externally
	let dndItems = $state<PlaylistItem[]>([]);
	$effect(() => {
		dndItems = items;
	});

	function setSort(mode: SortMode) {
		sortMode = mode;
		onfetchsort?.(modeToServerSort(mode));
	}

	const clickAddedHeader = () => setSort(nextDateSort(sortMode, readonly));
	const clickScoreHeader = () => setSort(nextScoreSort(sortMode, readonly));

	const displayItems = $derived(orderForDisplay(items, sortMode));

	// Keyboard navigation. Single letters only fire when nothing is being typed into, which is
	// most of what this app is made of — see isPlainKey.
	let focusedIndex = $state(-1);
	const focusedItem = $derived(focusedIndex >= 0 ? displayItems[focusedIndex] : undefined);

	function onKeydown(event: KeyboardEvent) {
		if (readonly && event.key !== 'j' && event.key !== 'k' && event.key !== 'o') return;
		if (!isPlainKey(event)) return;

		switch (event.key) {
			case 'j':
			case 'ArrowDown':
				event.preventDefault();
				focusedIndex = moveFocus(focusedIndex, 1, displayItems.length);
				scrollFocusedIntoView();
				break;
			case 'k':
			case 'ArrowUp':
				event.preventDefault();
				focusedIndex = moveFocus(focusedIndex, -1, displayItems.length);
				scrollFocusedIntoView();
				break;
			case 'o':
			case 'Enter':
				if (focusedItem) {
					event.preventDefault();
					window.open(focusedItem.link.url, '_blank', 'noopener');
				}
				break;
			case 'e':
				if (focusedItem && !readonly) {
					event.preventDefault();
					toggleWatched(focusedItem);
				}
				break;
			case 'x':
				if (focusedItem && !readonly) {
					event.preventDefault();
					toggleSelected(focusedItem.id);
				}
				break;
			case 'Escape':
				focusedIndex = -1;
				break;
		}
	}

	function scrollFocusedIntoView() {
		// After the row has actually moved, so the browser scrolls to where it is now.
		requestAnimationFrame(() => {
			document
				.querySelector('[data-item-focused="true"]')
				?.scrollIntoView({ block: 'nearest' });
		});
	}


	let noteEditId = $state<string | null>(null);
	let draftNote = $state('');
	let draftTags = $state('');
	let actionFlyoutId = $state<string | null>(null);
	/** What the last "copy share link" did, so the row can say so rather than silently succeeding. */
	let shareToast = $state<string | null>(null);

	async function setCover(item: PlaylistItem) {
		actionFlyoutId = null;

		const res = await api.patch(`/playlists/${playlistId}`, { coverLinkId: item.link.id });
		shareToast = res.ok ? 'Cover set.' : 'Could not set the cover.';
	}

	async function copyShareLink(item: PlaylistItem) {
		actionFlyoutId = null;

		// Already shared: the existing link is handed back rather than rotated, so anything
		// already sent keeps working.
		const res = await api.post(`/items/${item.id}/share`);
		if (!res.ok) {
			shareToast = 'Could not create a share link.';
			return;
		}

		const { token } = (await res.json()) as { token: string };
		const url = `${location.origin}/i/${token}`;
		item.shareToken = token;

		try {
			await navigator.clipboard.writeText(url);
			shareToast = 'Share link copied.';
		} catch {
			// Clipboard access is denied often enough (insecure origins, permissions) that the
			// link itself has to be recoverable from the message.
			shareToast = url;
		}
	}

	let shareItem = $state<PlaylistItem | null>(null);
	let shareOpen = $state(false);

	const FLIP = 150;
	const useDnd = $derived(canReorder(sortMode, readonly));

	function onConsider(e: CustomEvent<{ items: PlaylistItem[] }>) {
		dndItems = e.detail.items;
	}

	async function onFinalize(e: CustomEvent<{ items: PlaylistItem[]; info: { id: string } }>) {
		dndItems = e.detail.items;
		const movedId = e.detail.info.id;
		const idx = dndItems.findIndex((i) => i.id === movedId);
		const afterItemId = idx > 0 ? dndItems[idx - 1].id : null;
		items = dndItems;
		await api.post(`/items/${movedId}/move`, { afterItemId });
		await onmove?.();
	}

	async function saveNote(item: PlaylistItem) {
		const note = draftNote.trim() || null;
		// Comma-separated, because that is how the playlist tag editor already takes them.
		const tags = draftTags
			.split(',')
			.map((t) => t.trim())
			.filter(Boolean);

		const res = await api.patch(`/items/${item.id}`, { note, tags });
		if (res.ok) {
			const saved = (await res.json()) as PlaylistItem;
			items = items.map((i) => (i.id === item.id ? { ...i, note, tags: saved.tags ?? [] } : i));
		}
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
		if (res.ok || res.status === 204) {
			items = items.filter((i) => i.id !== item.id);
			if (total !== null) total = Math.max(0, total - 1);
		}
	}

	async function toggleWatched(item: PlaylistItem) {
		const newStatus = item.status === 'Watched' ? 'Added' : 'Watched';
		const res = await api.patch(`/items/${item.id}`, { status: newStatus });
		if (!res.ok) return;
		const updated = items.map((i) => (i.id === item.id ? { ...i, status: newStatus } : i));
		// If the active status filter now excludes this item, drop it from the visible list.
		if (statusFilter === 'Watched') {
			const next = updated.filter((i) => i.status === 'Watched');
			if (total !== null && next.length < updated.length) total = Math.max(0, total - 1);
			items = next;
		} else if (statusFilter === 'Unwatched') {
			const next = updated.filter((i) => i.status === 'Added');
			if (total !== null && next.length < updated.length) total = Math.max(0, total - 1);
			items = next;
		} else {
			items = updated;
		}
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
		data-item-focused={focusedItem?.id === item.id}
		style="border-color: var(--color-border); {item.status === 'Watched' ? 'opacity: 0.45' : ''}{focusedItem?.id === item.id ? '; box-shadow: inset 3px 0 0 var(--color-accent)' : ''}"
	>
		{#if !readonly}
			<td class="pr-1">
				<input
					type="checkbox"
					checked={selected.has(item.id)}
					onchange={() => toggleSelected(item.id)}
					aria-label={`Select ${item.link.title ?? item.link.url}`}
				/>
			</td>
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
						<!-- Served through our own host rather than hotlinked: rendering the origin
						     URL told every site in this playlist the viewer's IP and what they were
						     looking at, and broke whenever a host refused hotlinking. -->
						<img
							src={`/api/v1/thumbnails/${item.link.id}`}
							alt=""
							class="shrink-0 rounded object-cover"
							style="height: 5em; width: auto"
							loading="lazy"
							onerror={(e) => e.currentTarget.remove()}
						/>
					{:else if item.link.favicon}
						<!-- No page image: the site's own icon keeps the row's left edge aligned
						     with its neighbours instead of leaving a ragged gap. -->
						<span
							class="flex shrink-0 items-center justify-center rounded"
							style="height: 5em; width: 5em; background: var(--color-surface)"
						>
							<img
								src={item.link.favicon}
								alt=""
								class="size-6 object-contain"
								loading="lazy"
								onerror={(e) => e.currentTarget.parentElement?.remove()}
							/>
						</span>
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
					<KindBadge kind={item.link.kind} />
					{#if item.link.wordCount}
						<!-- The text was kept at enrichment, so this still works once the page
						     behind the link has gone. -->
						<a
							href={`/read/${item.link.id}`}
							class="ml-1.5 inline-flex items-center gap-1 align-middle text-xs hover:underline"
							style="color: var(--color-muted)"
							title="Read the saved article"
						>
							<BookOpen size={12} aria-hidden="true" />
							{readingLabel(item.link.wordCount)}
						</a>
					{/if}
					{#if item.metadata?.author}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.metadata.author}</p>
					{/if}
					{#if item.note && readonly}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.note}</p>
					{/if}
					{#if item.tags?.length}
						<span class="mt-1 flex flex-wrap gap-1">
							{#each item.tags as tag (tag)}
								<a
									href={`/search?itemTag=${encodeURIComponent(tag)}`}
									class="rounded px-1.5 py-0.5 text-xs hover:underline"
									style="background: var(--color-bg); color: var(--color-muted)"
									title={`Find everything tagged ${tag}`}
								>{tag}</a>
							{/each}
						</span>
					{/if}
					{#if item.link.enrichmentStatus === 'Failed' || item.link.enrichmentStatus === 'Broken'}
						<!-- Without this the row is a bare URL and nothing says why. -->
						<p class="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs" style="color: var(--color-muted)">
							<AlertCircle
								size={12}
								aria-hidden="true"
								style="color: {item.link.enrichmentStatus === 'Broken' ? 'var(--color-danger)' : 'var(--color-muted)'}"
							/>
							{item.link.enrichmentError ?? 'We could not read this page.'}
							{#if item.link.archiveUrl}
								<!-- The one moment archiving was for: the page is gone and there is
								     still somewhere to send someone. -->
								<a
									href={item.link.archiveUrl}
									target="_blank"
									rel="noreferrer"
									class="inline-flex items-center gap-1 underline underline-offset-2"
								>
									<Archive size={12} aria-hidden="true" />
									Archived copy
								</a>
							{/if}
							{#if !readonly}
								<button
									type="button"
									onclick={() => recheck(item)}
									disabled={rechecking.has(item.link.id)}
									class="underline underline-offset-2 disabled:no-underline disabled:opacity-60"
								>{rechecking.has(item.link.id) ? 'Trying…' : 'Try again'}</button>
							{/if}
						</p>
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
							{#if !readonly && playlistId && item.link.thumbnailUrl}
								<!-- Only offered for a link that has an image: the cover is one of the
								     playlist's own pictures, not a placeholder. -->
								<button
									type="button"
									onclick={() => setCover(item)}
									class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
									style="color: var(--color-muted)"
									title="Use as the playlist cover"
									aria-label="Use as the playlist cover"
								>
									<Image size={16} aria-hidden="true" />
								</button>
							{/if}
							{#if !readonly}
								<!-- A link someone else can open, carrying the note with it. Sharing one
								     link otherwise meant making the whole playlist public. -->
								<button
									type="button"
									onclick={() => copyShareLink(item)}
									class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
									style={item.shareToken ? 'color: var(--color-accent)' : 'color: var(--color-muted)'}
									title={item.shareToken ? 'Copy the share link' : 'Create a link to send'}
									aria-label={item.shareToken ? 'Copy the share link' : 'Create a link to send'}
								>
									<Link2 size={16} aria-hidden="true" />
								</button>
							{/if}
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
									else { noteEditId = item.id; draftNote = item.note ?? ''; draftTags = (item.tags ?? []).join(', '); }
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
					<div class="flex flex-1 flex-col gap-1.5">
						<textarea
							bind:value={draftNote}
							placeholder="Add note…"
							rows={3}
							class="resize-none rounded border px-2 py-1 text-sm"
							style="border-color: var(--color-border); background: var(--color-bg)"
							onkeydown={(e) => {
								if (e.key === 'Enter' && e.ctrlKey) { saveNote(item); noteEditId = null; }
								if (e.key === 'Escape') { noteEditId = null; }
							}}
						></textarea>
						<input
							bind:value={draftTags}
							placeholder="Tags, comma separated…"
							aria-label="Tags for this link"
							class="rounded border px-2 py-1 text-sm"
							style="border-color: var(--color-border); background: var(--color-bg)"
							onkeydown={(e) => {
								if (e.key === 'Enter') { saveNote(item); noteEditId = null; }
								if (e.key === 'Escape') { noteEditId = null; }
							}}
						/>
					</div>
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

<svelte:window onkeydown={onKeydown} />

{#if items.length > 0 || sourceFilter !== null || statusFilter !== 'All' || isSearching}
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
							onclick={() => { onstatusfilter?.(f); statusOpen = false; }}
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

		<!-- Layout switch. A reading queue reads best as a list; a list of videos does not. -->
		<div class="inline-flex divide-x overflow-hidden rounded-full border" style="border-color: var(--color-border)">
			{#each [['table', 'List', Rows3], ['grid', 'Grid', LayoutGrid]] as const as [mode, label, Icon] (mode)}
				<button
					type="button"
					onclick={() => { viewMode = mode; if (playlistId) savePrefs(playlistId, { viewMode: mode }); }}
					class="px-2.5 py-0.5"
					style={viewMode === mode
						? 'background: var(--color-surface); color: var(--color-accent)'
						: 'color: var(--color-muted)'}
					title={`${label} view`}
					aria-label={`${label} view`}
					aria-pressed={viewMode === mode}
				>
					<Icon size={13} aria-hidden="true" />
				</button>
			{/each}
		</div>

		<button
			type="button"
			onclick={() => { showThumbnails = !showThumbnails; if (playlistId) savePrefs(playlistId, { showThumbnails }); }}
			class="{toggleClass} inline-flex items-center gap-1"
			style={toggleStyle(showThumbnails)}
		>
			<Image size={11} aria-hidden="true" /> Thumbnail
		</button>

		{#if total !== null}
			<span class="ml-auto text-xs" style="color: var(--color-muted)">
				{total} {total === 1 ? 'item' : 'items'}
			</span>
		{/if}
	</div>
{/if}

{#if shareToast}
	<p
		class="mb-2 rounded-md border px-3 py-2 text-sm"
		style="border-color: var(--color-border); background: var(--color-surface)"
		role="status"
	>
		{shareToast}
		<button
			type="button"
			onclick={() => (shareToast = null)}
			class="ml-2 underline underline-offset-2"
			style="color: var(--color-muted)"
		>Dismiss</button>
	</p>
{/if}

{#if items.length === 0}
	{#if sourceFilter !== null || statusFilter !== 'All' || isSearching}
		<p class="py-6 text-center text-sm" style="color: var(--color-muted)">No matching items.</p>
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
{:else}
	{#if !readonly && playlistId}
		{@const count = selected.size}
		<PlaylistPickerDialog
			bind:open={moveOpen}
			title="Move to playlist"
			subtitle={`${count} ${count === 1 ? 'link' : 'links'} will move out of this playlist.`}
			excludePlaylistId={playlistId}
			onselect={async (target) => {
				await bulk({ action: 'Move', targetPlaylistId: target });
				return 'Moved';
			}}
		/>
		<PlaylistPickerDialog
			bind:open={copyOpen}
			title="Copy to playlist"
			subtitle={`${count} ${count === 1 ? 'link' : 'links'} will be copied, with their notes and scores.`}
			excludePlaylistId={playlistId}
			onselect={async (target) => {
				await bulk({ action: 'Copy', targetPlaylistId: target });
				return 'Copied';
			}}
		/>
	{/if}

	{#if !readonly && selected.size > 0}
		<!-- Appears only with a selection, so it never occupies space it hasn't earned. -->
		<div
			class="mb-2 flex flex-wrap items-center gap-2 rounded-md border px-3 py-2 text-sm"
			style="border-color: var(--color-accent); background: var(--color-surface)"
		>
			<span class="font-medium tabular-nums">{selected.size} selected</span>

			<button
				type="button"
				disabled={bulkBusy}
				onclick={() => bulk({ action: 'SetStatus', status: 'Watched' })}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				<Eye size={14} aria-hidden="true" /> Watched
			</button>

			<button
				type="button"
				disabled={bulkBusy}
				onclick={() => bulk({ action: 'SetStatus', status: 'Added' })}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				<EyeOff size={14} aria-hidden="true" /> Unwatched
			</button>

			{#if playlistId}
				<button
					type="button"
					disabled={bulkBusy}
					onclick={() => (moveOpen = true)}
					class="rounded-md border px-2.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
				>Move to…</button>
				<button
					type="button"
					disabled={bulkBusy}
					onclick={() => (copyOpen = true)}
					class="rounded-md border px-2.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
				>Copy to…</button>
			{/if}

			<button
				type="button"
				disabled={bulkBusy}
				onclick={bulkDelete}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border); color: var(--color-danger)"
			>
				<Trash2 size={14} aria-hidden="true" /> Delete
			</button>

			<button
				type="button"
				onclick={() => selected.clear()}
				class="ml-auto rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
				title="Clear selection"
				aria-label="Clear selection"
			>
				<X size={15} aria-hidden="true" />
			</button>
		</div>
	{/if}

	{#if viewMode === 'grid'}
		<!-- A list of videos or images is unreadable as a table of titles. Cards lead with the
		     picture, which is the thing being chosen between. -->
		<ul class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
			{#each displayItems as item (item.id)}
				{@const thumb = item.metadata?.thumbnail ?? item.link.thumbnailUrl}
				<li
					class="flex flex-col overflow-hidden rounded-lg border"
					data-item-focused={focusedItem?.id === item.id}
					style="border-color: {focusedItem?.id === item.id ? 'var(--color-accent)' : 'var(--color-border)'}; background: var(--color-surface); {item.status === 'Watched' ? 'opacity: 0.5' : ''}"
				>
					<a href={item.link.url} target="_blank" rel="noopener noreferrer" class="block">
						{#if thumb}
							<img
								src={`/api/v1/thumbnails/${item.link.id}`}
								alt=""
								class="aspect-video w-full object-cover"
								loading="lazy"
								onerror={(e) => e.currentTarget.remove()}
							/>
						{:else}
							<span
								class="flex aspect-video w-full items-center justify-center"
								style="background: var(--color-bg)"
							>
								{#if item.link.favicon}
									<img src={item.link.favicon} alt="" class="size-7 object-contain" loading="lazy" />
								{/if}
							</span>
						{/if}
					</a>

					<div class="flex min-w-0 flex-1 flex-col gap-1 p-3">
						<a
							href={item.link.url}
							target="_blank"
							rel="noopener noreferrer"
							class="line-clamp-2 text-sm font-medium hover:underline"
						>
							{item.metadata?.title ?? item.link.title ?? item.link.url}
						</a>

						{#if item.link.nsfw}<span><NsfwBadge /></span>{/if}

						<p class="mt-auto flex items-center gap-2 pt-1 text-xs" style="color: var(--color-muted)">
							<span class="truncate">{item.link.host}</span>
							{#if item.score !== null}
								<span class="ml-auto shrink-0 tabular-nums">{item.score}</span>
							{/if}
						</p>

						{#if !readonly}
							<button
								type="button"
								onclick={() => toggleWatched(item)}
								class="mt-1 w-full rounded border py-1 text-xs hover:bg-black/5 dark:hover:bg-white/10"
								style="border-color: var(--color-border); color: var(--color-muted)"
							>{item.status === 'Watched' ? 'Mark unwatched' : 'Mark watched'}</button>
						{/if}
					</div>
				</li>
			{/each}
		</ul>
	{:else}
		<div class="overflow-x-auto">
			<table class="w-full border-collapse text-sm">
				<thead>
					<tr class="text-left" style="color: var(--color-muted)">
						{#if !readonly}
							<th class="w-6">
								<input
									type="checkbox"
									checked={allSelected}
									indeterminate={selected.size > 0 && !allSelected}
									onchange={toggleSelectAll}
									aria-label="Select all"
								/>
							</th>
							<th class="w-6"></th>
						{/if}
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

	<p class="mt-3 hidden text-xs sm:block" style="color: var(--color-muted)">
		<kbd>j</kbd>/<kbd>k</kbd> to move, <kbd>o</kbd> to open{#if !readonly}, <kbd>e</kbd> to mark
			watched, <kbd>x</kbd> to select{/if}. <kbd>/</kbd> to jump anywhere.
	</p>
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
