<script lang="ts">
	import { failureMessage } from '$lib/api/errors';
	import Button, { buttonClass } from '$lib/components/ui/Button.svelte';
	import MenuSeparator from '$lib/components/ui/MenuSeparator.svelte';
	import MenuItem from '$lib/components/ui/MenuItem.svelte';
	import { toast } from '$lib/toast.svelte';
	import MenuRadio from '$lib/components/ui/MenuRadio.svelte';
	import Menu from '$lib/components/ui/Menu.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import Chip from '$lib/components/ui/Chip.svelte';
	import Checkbox from '$lib/components/ui/Checkbox.svelte';
	import Textarea from '$lib/components/ui/Textarea.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import { dragHandle, dragHandleZone } from 'svelte-dnd-action';
	import { SvelteSet } from 'svelte/reactivity';
	import { api } from '$lib/api/client';
	import { readingLabel } from '$lib/reading';
	import { AlertCircle, Archive, ArrowDown, ArrowUp, ArrowUpDown, BookOpen, Check, Link2, ChevronDown, Clock, Eye, EyeOff, FolderInput, GripVertical, Image, LayoutGrid, MoreVertical, Rows3, Rss, Star, StickyNote, Trash2, Type, X, ListPlus, ArrowUpToLine, Square, SquareCheck } from '@lucide/svelte';
	import PlaylistPickerDialog from './PlaylistPickerDialog.svelte';
	import PlaylistDropTray from './PlaylistDropTray.svelte';
	import NsfwBadge from './NsfwBadge.svelte';
	import KindBadge from './KindBadge.svelte';
	import { savePrefs } from '$lib/prefs';
	import { isPlainKey, moveFocus } from '$lib/keyboard';
	import { describeDrag, dragSet, encodePayload, ITEMS_MIME } from '$lib/dragItems';
	import { SORT_LABELS, canReorder, modeToServerSort, nextDateSort, nextScoreSort, orderForDisplay, serverSortToMode, type SortMode } from '$lib/sorting';
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
		onclearfilters,
		isSearching = false,
		shared = false,
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
		/** Puts the list back to everything: status, source and the search box together. */
		onclearfilters?: () => void;
		isSearching?: boolean;
		/** Whether this playlist has other people in it, which decides if "added by" is worth showing. */
		shared?: boolean;
		total?: number | null;
	} = $props();

	/**
	 * Whether to say who added each link.
	 *
	 * Attribution answers "who put this here", which is only ever a question on a playlist more
	 * than one person adds to. On a list somebody keeps alone, "added by you" on every row is
	 * noise. The rows carry the name regardless — the API has no view on whether it is
	 * interesting — so the decision is made here.
	 *
	 * Two ways to be sure: this playlist was shared with the viewer, or the rows themselves name
	 * more than one person. The second covers an owner, who otherwise has no way to know whether
	 * anybody else has contributed without asking the server a separate question.
	 */
	const showWhoAdded = $derived(
		shared || new Set(items.map((i) => i.addedBy).filter(Boolean)).size > 1
	);

	let sortMode = $state<SortMode>(serverSortToMode(initialPrefs?.sort, readonly));
	let showThumbnails = $state(initialPrefs?.showThumbnails ?? true);
	let viewMode = $state(initialPrefs?.viewMode === 'grid' ? 'grid' : 'table');
	let showUrls = $state(initialPrefs?.showUrls ?? false);
	let forceShowScore = $state(false);

	const statusOptions: StatusFilter[] = ['All', 'Unwatched', 'Watched'];

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

	/**
	 * Links whose thumbnail would not load.
	 *
	 * The image used to delete itself on error, which made "no picture was ever found" and "the
	 * server refused to serve the picture" look identical, and reflowed the row while you were
	 * reading it. Tracked instead, so the same favicon placeholder a link without an image gets
	 * is what appears — the layout holds still and nothing looks broken.
	 */
	let thumbnailFailed = new SvelteSet<string>();

	// Multi-select. Every action below already exists per item; the point is doing it to a
	// selection without repeating yourself forty times.
	let selected = new SvelteSet<string>();
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

	/** Whether the change went through — the picker needs to know before it says "Moved". */
	async function bulk(body: Record<string, unknown>): Promise<boolean> {
		if (selected.size === 0) return false;
		bulkBusy = true;
		try {
			const res = await api.post('/items/bulk', { itemIds: [...selected], ...body });
			if (res.ok) {
				selected.clear();
				await onmove?.();
				return true;
			}
			// Forty items not moving looks exactly like forty items moving and the page not
			// refreshing. Any of these can fail for real: a 409 from the concurrency token, a
			// 429, a 403 once a share role is revoked mid-session.
			warn(failureMessage(res.status, 'Could not do that to the selection.'));
			return false;
		} catch {
			warn('Could not reach the server.');
			return false;
		} finally {
			bulkBusy = false;
		}
	}

	/**
	 * The keyboard's way to the same place a drag would have gone.
	 *
	 * Selecting just this row first, so the picker's Move acts on it alone rather than on whatever
	 * happened to be checked already.
	 */
	function moveOne(item: PlaylistItem) {
		selected.clear();
		selected.add(item.id);
		moveOpen = true;
	}

	/** Marks the selection, and offers to put each back the way it was. */
	async function bulkStatus(status: 'Watched' | 'Added') {
		const before = items.filter((i) => selected.has(i.id)).map((i) => ({ id: i.id, status: i.status }));
		if (!(await bulk({ action: 'SetStatus', status }))) return;

		const changed = before.filter((b) => b.status !== status);
		if (changed.length === 0) return;
		toast.success(
			`Marked ${changed.length} ${changed.length === 1 ? 'link' : 'links'} ${status === 'Watched' ? 'watched' : 'unwatched'}.`,
			{
				action: {
					label: 'Undo',
					run: async () => {
						const res = await api.post('/items/bulk', {
							itemIds: changed.map((c) => c.id),
							action: 'SetStatus',
							status: status === 'Watched' ? 'Added' : 'Watched'
						});
						if (!res.ok) warn(failureMessage(res.status, 'Could not undo that.'));
						await onmove?.();
					}
				}
			}
		);
	}

	async function bulkDelete() {
		const count = selected.size;
		const ok = await confirmDialog(
			`Delete ${count} ${count === 1 ? 'link' : 'links'}? You can put them back from the trash.`,
			{ danger: true, confirmLabel: 'Delete' }
		);
		if (!ok) return;

		const ids = [...selected];
		if (await bulk({ action: 'Delete' })) {
			toast.success(`Moved ${count} ${count === 1 ? 'link' : 'links'} to the trash.`, {
				action: { label: 'Undo', run: () => restoreFromTrash(ids) }
			});
		}
	}

	/**
	 * Puts removed links back.
	 *
	 * Removing was instant and final-looking even though the links only went to the trash for
	 * thirty days — nothing said so, and getting one back meant finding the trash page.
	 */
	async function restoreFromTrash(ids: string[]) {
		const results = await Promise.all(ids.map((id) => api.post(`/trash/items/${id}/restore`).catch(() => null)));
		const failed = results.filter((res) => !res || (!res.ok && res.status !== 409)).length;
		await onmove?.();
		if (failed === 0) say(ids.length === 1 ? 'Put back.' : `Put ${ids.length} links back.`);
		else warn(`Could not put ${failed === 1 ? 'one of them' : `${failed} of them`} back. They are still in the trash.`);
	}

	// Links being re-fetched right now, so the button can say so rather than looking inert.
	let rechecking = new SvelteSet<string>();

	/** How long to give a recheck before looking at the link again. */
	const RECHECK_LOOK_AGAIN_MS = 6000;

	async function recheck(item: PlaylistItem) {
		rechecking.add(item.link.id);
		const res = await api.post(`/links/${item.link.id}/recheck`);
		if (!res.ok) {
			rechecking.delete(item.link.id);
			warn(failureMessage(res.status, 'Could not check that page again.'));
			return;
		}
		// Reading the page happens in the background, so there is nothing truthful to show at
		// once. It used to say "Trying…" until the page was reloaded by hand; now the list is
		// fetched again after a few seconds, which shows the result if there is one by then.
		say('Checking that page again.');
		setTimeout(async () => {
			rechecking.delete(item.link.id);
			await onmove?.();
		}, RECHECK_LOOK_AGAIN_MS);
	}

	// What the drag library reorders. A writable $derived: it follows `items`, and the reordering
	// the library does during a drag stands until `items` changes again — which is the same
	// intent the $state + $effect pair had, without the extra render pass.
	let dndItems = $derived(items);

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
	/**
	 * Feedback about the last thing that happened, through the app's toasts.
	 *
	 * Every write on this table used to discard its own failures, so a refused delete, a rejected
	 * rating and a bulk move that moved nothing all looked identical to success. Errors stay until
	 * dismissed and are announced assertively, because a polite one goes unread by exactly the
	 * person who most needs it.
	 */
	const say = (text: string) => toast.success(text);
	const warn = (text: string) => toast.error(text);

	async function setCover(item: PlaylistItem) {
		const res = await api.patch(`/playlists/${playlistId}`, { coverLinkId: item.link.id });
		if (res.ok) say('Cover set.');
		else warn(failureMessage(res.status, 'Could not set the cover.'));
	}

	/**
	 * "Half read", when there is something to continue.
	 *
	 * Only for items actually in progress: an untouched one says how long it will take, which is
	 * the more useful thing to know, and a finished one is already marked watched.
	 */
	function startedLabel(item: PlaylistItem): string | null {
		const progress = item.readProgress ?? 0;
		if (progress <= 0.02 || progress >= 0.92) return null;

		return `${Math.round(progress * 100)}% read`;
	}

	async function copyShareLink(item: PlaylistItem) {
		// Already shared: the existing link is handed back rather than rotated, so anything
		// already sent keeps working.
		const res = await api.post(`/items/${item.id}/share`);
		if (!res.ok) {
			warn('Could not create a share link.');
			return;
		}

		const { token } = (await res.json()) as { token: string };
		const url = `${location.origin}/i/${token}`;
		item.shareToken = token;

		try {
			await navigator.clipboard.writeText(url);
			say('Share link copied.');
		} catch {
			// Clipboard access is denied often enough (insecure origins, permissions) that the
			// link itself has to be recoverable from the message — so it stays until dismissed.
			toast.info(url, { duration: null });
		}
	}

	let shareItem = $state<PlaylistItem | null>(null);
	let shareOpen = $state(false);

	const FLIP = 150;
	const useDnd = $derived(canReorder(sortMode, readonly));

	/**
	 * Starts a drag towards another playlist.
	 *
	 * Reordering is the handle's job now, so dragging the row means taking it out of here. The two
	 * used to be the same gesture, which made the handle decorative and left no way to express
	 * the more useful of the two.
	 */
	function onRowDragStart(e: DragEvent, item: PlaylistItem) {
		if (readonly || !playlistId || !e.dataTransfer) return;

		const itemIds = dragSet(item.id, selected);
		const label = describeDrag(itemIds.length, item.metadata?.title ?? item.link.title);

		e.dataTransfer.setData(
			ITEMS_MIME,
			encodePayload({ fromPlaylistId: playlistId, itemIds, label })
		);
		// Both offered, so the browser's own cursor agrees with the modifier the person is holding.
		e.dataTransfer.effectAllowed = 'copyMove';
	}

	function onConsider(e: CustomEvent<{ items: PlaylistItem[] }>) {
		dndItems = e.detail.items;
	}

	/**
	 * Reordering without a mouse.
	 *
	 * The drag handle was the only way to change the order, and the drag library turned the
	 * table into a list for screen readers while it was at it. These go through the same move the
	 * drag makes, from the row's menu.
	 */
	async function moveWithin(item: PlaylistItem, to: 'up' | 'down' | 'top') {
		const order = [...dndItems];
		const index = order.findIndex((i) => i.id === item.id);
		if (index < 0) return;

		const target = to === 'top' ? 0 : to === 'up' ? index - 1 : index + 1;
		if (target < 0 || target >= order.length || target === index) return;

		order.splice(index, 1);
		order.splice(target, 0, item);
		const afterItemId = target > 0 ? order[target - 1].id : null;

		items = order;
		const res = await api.post(`/items/${item.id}/move`, { afterItemId });
		if (!res.ok) {
			warn(failureMessage(res.status, 'Could not move that.'));
			await onmove?.();
			return;
		}
		say(to === 'top' ? 'Moved to the top.' : to === 'up' ? 'Moved up.' : 'Moved down.');
		await onmove?.();
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
		} else {
			// Said out loud, because the editor closes on blur and the note is gone with it.
			warn(failureMessage(res.status, 'Could not save that note.'));
		}
	}

	// Returns the saved score (or undefined if nothing changed / invalid).
	async function saveScore(item: PlaylistItem, rawValue: string): Promise<number | null | undefined> {
		const score = rawValue === '' ? null : Math.max(0, Math.min(100, parseInt(rawValue, 10)));
		if (score !== null && isNaN(score)) return undefined;
		if (score === item.score) return item.score;

		const res = await api.put(`/items/${item.id}/score`, { score });
		if (!res.ok) {
			// The caller snaps the input to what this returns, so returning the requested value
			// after a failed write left a rating on screen that the server had never heard of.
			warn(failureMessage(res.status, 'Could not save that rating.'));
			return item.score;
		}

		items = items.map((i) => (i.id === item.id ? { ...i, score } : i));
		return score;
	}

	async function remove(item: PlaylistItem) {
		const res = await api.del(`/items/${item.id}`);
		if (res.ok || res.status === 204) {
			items = items.filter((i) => i.id !== item.id);
			if (total !== null) total = Math.max(0, total - 1);
			toast.success('Moved to the trash.', { action: { label: 'Undo', run: () => restoreFromTrash([item.id]) } });
		} else {
			warn(failureMessage(res.status, 'Could not remove that link.'));
		}
	}

	async function toggleWatched(item: PlaylistItem) {
		const newStatus = item.status === 'Watched' ? 'Added' : 'Watched';
		const res = await api.patch(`/items/${item.id}`, { status: newStatus });
		if (!res.ok) {
			// Returning quietly left the checkbox snapping back with no reason given, which reads
			// as the app being broken rather than as the write being refused.
			warn(failureMessage(res.status, 'Could not mark that.'));
			return;
		}
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

	/** Whether anything is selected: what brings the checkbox column back on a narrow screen. */
	const selecting = $derived(selected.size > 0);

	const toggleClass = 'inline-flex min-h-6 items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs transition-colors';

	/** A filter or sort chip: accent when it narrows or reorders what is shown. */
	function chipTrigger(active: boolean) {
		return `${toggleClass} ${active ? 'border-accent text-accent' : 'border-border text-muted'}`;
	}
</script>

{#snippet row(item: PlaylistItem, draggable: boolean)}
	<tr
		class="border-t align-middle"
		data-item-focused={focusedItem?.id === item.id}
		data-item-id={item.id}
		data-watched={item.status === 'Watched'}
		style="border-color: var(--color-border);{focusedItem?.id === item.id ? ' box-shadow: inset 3px 0 0 var(--color-accent)' : ''}"
	>
		{#if !readonly}
			<!-- At phone width the checkbox appears only once something is being selected, and the
			     row menu is how that starts: a checkbox, two drag grips, a thumbnail and a date
			     column left about sixty pixels for the title, which wrapped one letter at a time. -->
			<td class="pr-1 {selecting ? '' : 'hidden sm:table-cell'}">
				<Checkbox
					checked={selected.has(item.id)}
					onchange={() => toggleSelected(item.id)}
					label={`Select ${item.link.title ?? item.link.url}`}
				/>
			</td>
			<!-- Two grips, because they are two different things and sharing one gesture between
			     them would make both ambiguous. The left reorders within this playlist; the right
			     takes the row out of it. Each is labelled, and each does only its own job. -->
			<td class="hidden select-none whitespace-nowrap pr-1 sm:table-cell" style="color: var(--color-muted)">
				{#if draggable}
					<span
						use:dragHandle
						class="cursor-grab align-middle"
						title="Drag to reorder"
						aria-label={`Reorder ${item.link.title ?? item.link.url}`}
					>
						<GripVertical size={14} aria-hidden="true" />
					</span>
				{/if}
				{#if playlistId}
					<!-- draggable is set on this element rather than the row: svelte-dnd-action
					     overwrites the row's own attribute to keep reordering to its handle.
					     A button rather than a span, because a control that can only be dragged
					     cannot be used from a keyboard at all — pressing it opens the same
					     picker the drag would have dropped onto. -->
					<button
						type="button"
						draggable="true"
						ondragstart={(e) => onRowDragStart(e, item)}
						onclick={() => moveOne(item)}
						class="cursor-grab align-middle"
						title="Drag to another playlist, or press to choose one"
						aria-label={`Move ${item.link.title ?? item.link.url} to another playlist`}
					>
						<FolderInput size={13} aria-hidden="true" />
					</button>
				{/if}
			</td>
		{/if}
		<td class="py-2 pr-3">
			<div class="flex items-center gap-4">
				{#if showThumbnails}
					{@const thumb =
						(item.metadata?.thumbnail ?? item.link.thumbnailUrl) &&
						!thumbnailFailed.has(item.link.id)}
					{#if thumb}
						<!-- Served through our own host rather than hotlinked: rendering the origin
						     URL told every site in this playlist the viewer's IP and what they were
						     looking at, and broke whenever a host refused hotlinking. -->
						<img
							src={`/api/v1/thumbnails/${item.link.id}`}
							alt=""
							class="shrink-0 rounded object-cover"
							style="height: 5em; width: auto{item.status === 'Watched' ? '; opacity: 0.6' : ''}"
							loading="lazy"
							onerror={() => thumbnailFailed.add(item.link.id)}
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
						style={item.status === 'Watched' ? 'color: var(--color-muted)' : ''}
					>
						{showUrls ? item.link.url : (item.metadata?.title ?? item.link.title ?? item.link.url)}
					</a>
					{#if item.status === 'Watched'}
						<!-- Said, not faded: the whole row used to go to 45% opacity, which took its
						     text — the muted metadata most of all — well under a readable contrast. -->
						<span
							class="ml-1.5 inline-flex items-center gap-0.5 align-middle text-xs"
							style="color: var(--color-muted)"
						>
							<Check size={12} aria-hidden="true" /> Watched
						</span>
					{/if}
					{#if item.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}
					<KindBadge kind={item.link.kind} />
					{#if item.link.wordCount}
						<!-- The text was kept at enrichment, so this still works once the page
						     behind the link has gone. `from` is what lets the reader offer the
						     next and previous item in this list. -->
						<a
							href={`/read/${item.link.id}${playlistId ? `?from=${playlistId}` : ''}`}
							class="ml-1.5 inline-flex items-center gap-1 align-middle text-xs hover:underline"
							style="color: var(--color-muted)"
							title={startedLabel(item) ?? 'Read the saved article'}
						>
							<BookOpen size={12} aria-hidden="true" />
							{startedLabel(item) ?? readingLabel(item.link.wordCount)}
						</a>
					{/if}
					{#if item.metadata?.author}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.metadata.author}</p>
					{/if}
					<!-- Only where the column it belongs to is hidden. -->
					<p class="mt-0.5 text-xs text-muted sm:hidden">Added {dateAdded(item.creationTime)}</p>
					{#if showWhoAdded && item.addedBy}
						<!-- Only on a playlist more than one person adds to. On a list somebody keeps
						     alone, "added by you" on every row is noise saying nothing. -->
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">
							added by {item.addedBy}
						</p>
					{/if}
					{#if item.note && readonly}
						<p class="mt-0.5 text-xs" style="color: var(--color-muted)">{item.note}</p>
					{/if}
					{#if item.tags?.length}
						<span class="mt-1 flex flex-wrap gap-1">
							{#each item.tags as tag (tag)}
								<Chip href={`/search?itemTag=${encodeURIComponent(tag)}`} title={`Find everything tagged ${tag}`}>
									{tag}
								</Chip>
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
		<td class="hidden whitespace-nowrap text-center sm:table-cell" style="color: var(--color-muted)">{dateAdded(item.creationTime)}</td>
		{#if showScoreCol}
			<td class="w-10 pl-6 text-center" style="color: var(--color-muted)">
				<input
					type="number"
					min="0"
					max="100"
					value={item.score ?? ''}
					placeholder="—"
					aria-label={`Score for ${item.metadata?.title ?? item.link.title ?? item.link.url}, 0 to 100`}
					title="Score, 0 to 100"
					class="w-10 rounded-control border border-transparent bg-transparent text-center text-sm [appearance:textfield] hover:border-border-strong focus:border-accent focus:bg-bg [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
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
				<!-- A menu of labelled actions rather than a strip of seven bare icons: on a phone
				     there are no tooltips, and an icon for "add to another playlist" that looked like
				     "share" was a guess every time. -->
				<Menu
					triggerClass={buttonClass('ghost', 'sm', true, 'text-muted')}
					label={`Actions for ${item.metadata?.title ?? item.link.title ?? item.link.url}`}
					title="Actions"
					align="end"
					width="w-60"
				>
					{#snippet trigger()}
						<MoreVertical size={16} aria-hidden="true" />
					{/snippet}
					<MenuItem icon={item.status === 'Watched' ? EyeOff : Eye} onselect={() => toggleWatched(item)}>
						{item.status === 'Watched' ? 'Mark as unwatched' : 'Mark as watched'}
					</MenuItem>
					{#if item.link.wordCount}
						<MenuItem icon={BookOpen} href={`/read/${item.link.id}${playlistId ? `?from=${playlistId}` : ''}`}>
							Open in the reader
						</MenuItem>
					{/if}
					<MenuItem
						icon={isPending(item) ? Clock : StickyNote}
						onselect={() => {
							if (noteEditId === item.id) {
								noteEditId = null;
							} else {
								noteEditId = item.id;
								draftNote = item.note ?? '';
								draftTags = (item.tags ?? []).join(', ');
							}
						}}
					>
						{item.note ? 'Edit note and tags' : 'Add a note or tags'}
					</MenuItem>
					{#if useDnd}
						<MenuSeparator />
						<MenuItem icon={ArrowUpToLine} onselect={() => moveWithin(item, 'top')}>Move to the top</MenuItem>
						<MenuItem icon={ArrowUp} onselect={() => moveWithin(item, 'up')}>Move up</MenuItem>
						<MenuItem icon={ArrowDown} onselect={() => moveWithin(item, 'down')}>Move down</MenuItem>
					{/if}
					<MenuSeparator />
					<!-- On a phone this is the only way in to selecting: the column it lives in is
					     not shown until something is selected. -->
					<MenuItem
						icon={selected.has(item.id) ? SquareCheck : Square}
						onselect={() => toggleSelected(item.id)}
					>
						{selected.has(item.id) ? 'Take out of the selection' : 'Select this'}
					</MenuItem>
					<MenuItem icon={ListPlus} onselect={() => { shareItem = item; shareOpen = true; }}>
						Add to another playlist…
					</MenuItem>
					{#if playlistId}
						<MenuItem icon={FolderInput} onselect={() => moveOne(item)}>Move to another playlist…</MenuItem>
					{/if}
					<!-- A link someone else can open, carrying the note with it. Sharing one link
					     otherwise meant making the whole playlist public. -->
					<MenuItem icon={Link2} onselect={() => copyShareLink(item)}>
						{item.shareToken ? 'Copy its share link' : 'Create a link to send'}
					</MenuItem>
					{#if playlistId && item.link.thumbnailUrl}
						<!-- Only offered for a link that has an image: the cover is one of the
						     playlist's own pictures, not a placeholder. -->
						<MenuItem icon={Image} onselect={() => setCover(item)}>Use as the playlist cover</MenuItem>
					{/if}
					{#if !showScoreCol}
						<MenuItem icon={Star} onselect={() => (forceShowScore = true)}>Show the score column</MenuItem>
					{/if}
					<MenuSeparator />
					<MenuItem icon={Trash2} danger onselect={() => remove(item)}>Remove from this playlist</MenuItem>
				</Menu>
			</td>
		{/if}
	</tr>
	{#if !readonly && noteEditId === item.id}
		{@const colCount = 5 + (showScoreCol ? 1 : 0)}
		<tr class="border-t" style="border-color: var(--color-border)">
			<td colspan={colCount} class="px-2 py-2">
				<div class="flex items-start gap-2">
					<div class="flex flex-1 flex-col gap-1.5">
						<Textarea
							bind:value={draftNote}
							placeholder="Add note…"
							rows={3}
							onkeydown={(e) => {
								if (e.key === 'Enter' && e.ctrlKey) { saveNote(item); noteEditId = null; }
								if (e.key === 'Escape') { noteEditId = null; }
							}}
							size="sm"
						/>
						<Input
							bind:value={draftTags}
							placeholder="Tags, comma separated…"
							aria-label="Tags for this link"
							onkeydown={(e) => {
								if (e.key === 'Enter') { saveNote(item); noteEditId = null; }
								if (e.key === 'Escape') { noteEditId = null; }
							}}
							size="sm"
						/>
					</div>
					<div class="flex flex-col gap-1">
						<Button
							variant="primary"
							size="sm"
							icon={Check}
							onclick={() => { saveNote(item); noteEditId = null; }}
						>
							Save
						</Button>
						<Button size="sm" variant="ghost" onclick={() => { noteEditId = null; }}>Cancel</Button>
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
			<Menu triggerClass={chipTrigger(sourceFilter !== null)} title="Filter by source">
				{#snippet trigger()}
					<Rss size={12} aria-hidden="true" />
					<span class="sr-only">Source:</span>
					{sourceFilterLabel}
					<ChevronDown size={12} aria-hidden="true" />
				{/snippet}
				<MenuRadio
					value={sourceFilter ?? ''}
					options={[
						{ value: '', label: 'All sources' },
						{ value: 'manual', label: 'Added by hand' },
						...attachedSources.map((source) => ({ value: source.id, label: source.name }))
					]}
					onchange={(value) => onsourcefilter?.(value === '' ? null : value)}
				/>
			</Menu>
		{/if}
		{#if !readonly}
			<Menu triggerClass={chipTrigger(statusFilter !== 'Unwatched')} title="Filter by status">
				{#snippet trigger()}
					<Eye size={12} aria-hidden="true" />
					<span class="sr-only">Status:</span>
					{statusFilter}
					<ChevronDown size={12} aria-hidden="true" />
				{/snippet}
				<MenuRadio
					value={statusFilter}
					options={statusOptions.map((status) => ({ value: status, label: status }))}
					onchange={(status) => onstatusfilter?.(status)}
				/>
			</Menu>
		{/if}
		{#if attachedSources.length > 0 || !readonly}
			<span class="text-xs" style="color: var(--color-border)" aria-hidden="true">|</span>
		{/if}

		<!-- Second section: sort + display options -->
		<Menu triggerClass={chipTrigger(sortMode !== (readonly ? 'date-desc' : 'manual'))} title="Sort">
			{#snippet trigger()}
				<ArrowUpDown size={12} aria-hidden="true" />
				<span class="sr-only">Sort:</span>
				{SORT_LABELS[sortMode]}
				<ChevronDown size={12} aria-hidden="true" />
			{/snippet}
			<MenuRadio
				value={sortMode}
				options={[
					...(readonly ? [] : [{ value: 'manual' as SortMode, label: 'Manual' }]),
					{ value: 'date-asc' as SortMode, label: 'Oldest first' },
					{ value: 'date-desc' as SortMode, label: 'Newest first' },
					{ value: 'shuffle' as SortMode, label: 'Shuffle' },
					...(showScoreCol
						? [
								{ value: 'score-desc' as SortMode, label: 'Highest score' },
								{ value: 'score-asc' as SortMode, label: 'Lowest score' }
							]
						: [])
				]}
				onchange={setSort}
			/>
		</Menu>

		<span class="text-xs" style="color: var(--color-border)" aria-hidden="true">|</span>

		<Menu triggerClass={chipTrigger(showUrls)} title="Show titles or addresses">
			{#snippet trigger()}
				<Type size={12} aria-hidden="true" />
				<span class="sr-only">Show:</span>
				{showUrls ? 'URL' : 'Title'}
				<ChevronDown size={12} aria-hidden="true" />
			{/snippet}
			<MenuRadio
				value={showUrls ? 'url' : 'title'}
				options={[
					{ value: 'title', label: 'Titles' },
					{ value: 'url', label: 'Addresses' }
				]}
				onchange={(value) => {
					showUrls = value === 'url';
					if (playlistId) savePrefs(playlistId, { showUrls });
				}}
			/>
		</Menu>

		<!-- Layout switch. A reading queue reads best as a list; a list of videos does not. -->
		<SegmentedControl
			label="Layout"
			size="sm"
			options={[
				{ value: 'table', label: 'List view', icon: Rows3, iconOnly: true },
				{ value: 'grid', label: 'Grid view', icon: LayoutGrid, iconOnly: true }
			]}
			bind:value={viewMode}
			onchange={(mode) => { if (playlistId) savePrefs(playlistId, { viewMode: mode }); }}
		/>

		<button
			type="button"
			onclick={() => { showThumbnails = !showThumbnails; if (playlistId) savePrefs(playlistId, { showThumbnails }); }}
			class={chipTrigger(showThumbnails)}
			aria-pressed={showThumbnails}
		>
			<Image size={12} aria-hidden="true" /> Thumbnails
		</button>

		{#if total !== null}
			<span class="ml-auto text-xs" style="color: var(--color-muted)">
				{total} {total === 1 ? 'item' : 'items'}
			</span>
		{/if}
	</div>
{/if}


{#if items.length === 0}
	{#if sourceFilter !== null || statusFilter !== 'All' || isSearching}
		<div class="py-6 text-center">
			<p class="text-sm text-muted">Nothing here matches those filters.</p>
			{#if onclearfilters}
				<div class="mt-3 flex justify-center">
					<Button size="sm" icon={X} onclick={onclearfilters}>Clear filters</Button>
				</div>
			{/if}
		</div>
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
				// Throwing leaves the row as it was: a move that failed, or that had nothing left
				// to move, must not be reported as one that happened.
				if (!(await bulk({ action: 'Move', targetPlaylistId: target }))) throw new Error();
				// The selection has gone with it, so there is nothing more to pick a place for.
				moveOpen = false;
				return 'Moved';
			}}
		/>
		<PlaylistPickerDialog
			bind:open={copyOpen}
			title="Copy to playlist"
			subtitle={`${count} ${count === 1 ? 'link' : 'links'} will be copied, with their notes and scores.`}
			excludePlaylistId={playlistId}
			onselect={async (target) => {
				if (!(await bulk({ action: 'Copy', targetPlaylistId: target }))) throw new Error();
				copyOpen = false;
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
				onclick={() => bulkStatus('Watched')}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				<Eye size={14} aria-hidden="true" /> Watched
			</button>

			<button
				type="button"
				disabled={bulkBusy}
				onclick={() => bulkStatus('Added')}
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
				{@const thumb =
					(item.metadata?.thumbnail ?? item.link.thumbnailUrl) &&
					!thumbnailFailed.has(item.link.id)}
				<!-- The grid has no reorder zone, so the card itself can be the drag source —
				     nothing else is competing for the gesture here. -->
				<li
					class="flex flex-col overflow-hidden rounded-lg border"
					draggable={!readonly && playlistId ? 'true' : 'false'}
					ondragstart={(e) => onRowDragStart(e, item)}
					data-item-focused={focusedItem?.id === item.id}
					data-item-id={item.id}
					style="border-color: {focusedItem?.id === item.id ? 'var(--color-accent)' : 'var(--color-border)'}; background: var(--color-surface)"
				>
					<a href={item.link.url} target="_blank" rel="noopener noreferrer" class="block">
						{#if thumb}
							<img
								src={`/api/v1/thumbnails/${item.link.id}`}
								alt=""
								class="aspect-video w-full object-cover"
								style={item.status === 'Watched' ? 'opacity: 0.6' : ''}
								loading="lazy"
								onerror={() => thumbnailFailed.add(item.link.id)}
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
							style={item.status === 'Watched' ? 'color: var(--color-muted)' : ''}
						>
							{item.metadata?.title ?? item.link.title ?? item.link.url}
						</a>
						{#if item.status === 'Watched'}
							<span class="inline-flex items-center gap-0.5 text-xs" style="color: var(--color-muted)">
								<Check size={12} aria-hidden="true" /> Watched
							</span>
						{/if}

						{#if item.link.nsfw}<span><NsfwBadge /></span>{/if}

						<p class="mt-auto flex items-center gap-2 pt-1 text-xs" style="color: var(--color-muted)">
							<span class="truncate">{item.link.host}</span>
							{#if !readonly && playlistId}
								<!-- Visible and pressable, so the drag is discoverable and the card
								     is still usable without a pointer. -->
								<button
									type="button"
									onclick={() => moveOne(item)}
									class="ml-auto shrink-0"
									title="Drag the card to another playlist, or press to choose one"
									aria-label={`Move ${item.link.title ?? item.link.url} to another playlist`}
								>
									<FolderInput size={13} aria-hidden="true" />
								</button>
							{/if}
							{#if item.score !== null}
								<span class="shrink-0 tabular-nums" class:ml-auto={readonly || !playlistId}>{item.score}</span>
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
							<th class="w-6 {selecting ? '' : 'hidden sm:table-cell'}">
								<Checkbox
									checked={allSelected}
									indeterminate={selected.size > 0 && !allSelected}
									onchange={toggleSelectAll}
									label="Select all"
								/>
							</th>
							<th class="hidden w-6 sm:table-cell"></th>
						{/if}
						<th class="py-2 font-medium">{showUrls ? 'URL' : 'Title'}</th>
						<th class="hidden py-2 text-center font-medium sm:table-cell">
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
						use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP, dropTargetStyle: {}, autoAriaDisabled: true }}
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
	<!-- Appears only while one of our drags is in flight, so it costs nothing the rest of the
	     time and is nonetheless in a predictable place when it matters. -->
	<PlaylistDropTray
		{playlistId}
		ondropped={async () => {
			selected.clear();
			await onmove?.();
		}}
	/>
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
