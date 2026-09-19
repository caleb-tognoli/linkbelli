<script lang="ts">
	import { toast } from '$lib/toast.svelte';
	import { api, json } from '$lib/api/client';
	import { EyeOff, Globe, Lock } from '@lucide/svelte';
	import {
		bulkActionFor,
		decodePayload,
		describeResult,
		dropMode,
		ITEMS_MIME,
		type DropMode,
		type ItemDragPayload
	} from '$lib/dragItems';
	import type { Paged, Playlist } from '$lib/types';

	const visIcons = { Private: Lock, Unlisted: EyeOff, Public: Globe } as const;

	/**
	 * How many playlists the tray offers. A drag cannot type, so this is the shortlist people
	 * actually reach for; the Move dialog still handles the long tail, and the tray says so.
	 */
	const SHORTLIST = 8;

	let {
		/** The playlist being dragged out of, which is never a useful place to drop. */
		playlistId,
		ondropped
	}: {
		playlistId: string;
		/** Called after a successful drop, so the page can reload what it is showing. */
		ondropped?: () => Promise<void> | void;
	} = $props();

	let dragging = $state(false);
	let playlists = $state<Playlist[]>([]);
	let mode = $state<DropMode>('move');
	let over = $state<string | null>(null);
	let busy = $state(false);

	// Fetched once and kept: a drag is not the moment to wait on a request, and the list of
	// playlists does not change between two drags.
	let loaded = false;

	async function loadOnce() {
		if (loaded) return;
		loaded = true;

		const res = await api.get('/playlists?limit=30');
		if (!res.ok) return;

		const paged = await json<Paged<Playlist>>(res);
		playlists = paged.items.filter((p) => p.id !== playlistId).slice(0, SHORTLIST);
	}

	/**
	 * The tray follows the whole document's drag, not its own.
	 *
	 * A drop target that only appears once the pointer is already over it cannot be found: the
	 * person has to know it is there before they start moving. So this listens at the document
	 * level and shows itself the moment one of our own drags begins.
	 */
	function ourDrag(e: DragEvent) {
		return e.dataTransfer?.types.includes(ITEMS_MIME) ?? false;
	}

	function onDragStart(e: DragEvent) {
		if (!ourDrag(e)) return;

		dragging = true;
		loadOnce();
	}

	function onDragEnd() {
		dragging = false;
		over = null;
	}

	function onDragOverDocument(e: DragEvent) {
		if (!ourDrag(e)) return;

		// Read live rather than at dragstart: people decide to copy partway through a drag, and
		// the tray has to say which one it is about to do.
		mode = dropMode(e);
	}

	function onRowDragOver(e: DragEvent, playlist: Playlist) {
		if (!ourDrag(e)) return;

		// Without this the browser refuses the drop and shows a "no entry" cursor.
		e.preventDefault();
		if (e.dataTransfer) e.dataTransfer.dropEffect = mode === 'copy' ? 'copy' : 'move';
		over = playlist.id;
	}

	async function onRowDrop(e: DragEvent, playlist: Playlist) {
		e.preventDefault();
		over = null;

		const payload = decodePayload(e.dataTransfer?.getData(ITEMS_MIME));
		if (!payload) return;

		// Captured before the await: the modifier state is gone by the time the request returns.
		const dropped: DropMode = mode;
		await apply(payload, playlist, dropped);
	}

	async function apply(payload: ItemDragPayload, playlist: Playlist, dropped: DropMode) {
		busy = true;
		try {
			const res = await api.post('/items/bulk', {
				itemIds: payload.itemIds,
				action: bulkActionFor(dropped),
				targetPlaylistId: playlist.id
			});

			if (!res.ok) {
				toast.error('That did not work.');
				return;
			}

			const result = await json<{ affected: number; skipped: number }>(res);
			toast.success(describeResult(dropped, result.affected, result.skipped, playlist.name));

			// Only when something actually changed — a drop that moved nothing has not invalidated
			// what the page is showing.
			if (result.affected > 0) await ondropped?.();
		} catch {
			toast.error('That did not work.');
		} finally {
			busy = false;
			dragging = false;
		}
	}
</script>

<svelte:document
	ondragstart={onDragStart}
	ondragend={onDragEnd}
	ondragover={onDragOverDocument}
/>

{#if dragging || busy}
	<!-- Fixed to the edge of the viewport rather than placed in the page: a drag can start from a
	     row that has been scrolled to anywhere, and the target has to be somewhere predictable. -->
	<aside
		class="fixed bottom-4 right-4 z-50 w-72 max-w-[calc(100vw-2rem)] overflow-hidden rounded-lg border shadow-lg"
		style="border-color: var(--color-border); background: var(--color-surface)"
		aria-label="Drop onto a playlist"
	>
		<div class="border-b px-3 py-2 text-xs" style="border-color: var(--color-border)">
			<span class="font-medium">
				{mode === 'copy' ? 'Copy to…' : 'Move to…'}
			</span>
			<span style="color: var(--color-muted)">
				{mode === 'copy' ? ' release to copy' : ' hold Ctrl to copy'}
			</span>
		</div>

		{#if playlists.length === 0}
			<p class="px-3 py-4 text-sm" style="color: var(--color-muted)">
				Nowhere else to put things yet — this is your only playlist.
			</p>
		{:else}
			<ul class="max-h-72 overflow-y-auto">
				{#each playlists as playlist (playlist.id)}
					{@const Icon = visIcons[playlist.visibility]}
					<li>
						<!-- Not a button: this is a drop target, and a drag never clicks it. -->
						<div
							role="presentation"
							ondragover={(e) => onRowDragOver(e, playlist)}
							ondragleave={() => (over === playlist.id ? (over = null) : null)}
							ondrop={(e) => onRowDrop(e, playlist)}
							class="flex items-center gap-2 px-3 py-2 text-sm"
							style={over === playlist.id
								? 'background: var(--color-accent-solid); color: var(--color-on-solid)'
								: ''}
						>
							<Icon size={13} aria-hidden="true" style="opacity: 0.6" />
							<span class="truncate">{playlist.name}</span>
							<span class="ml-auto shrink-0 tabular-nums text-xs" style="opacity: 0.7">
								{playlist.itemCount}
							</span>
						</div>
					</li>
				{/each}
			</ul>
			<p class="border-t px-3 py-1.5 text-xs" style="border-color: var(--color-border); color: var(--color-muted)">
				Somewhere else? Select and use Move.
			</p>
		{/if}
	</aside>
{/if}

