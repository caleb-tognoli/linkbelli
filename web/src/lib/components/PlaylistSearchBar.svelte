<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { api } from '$lib/api/client';
	import { Plus, Search } from '@lucide/svelte';
	import { looksLikeUrl } from '$lib/urls';
	import type { LinkPreview, PlaylistItem } from '$lib/types';

	let {
		playlistId,
		isOwner,
		query = $bindable(''),
		resultCount,
		onAdded
	}: {
		playlistId: string;
		isOwner: boolean;
		query: string;
		resultCount: number;
		onAdded: (item: PlaylistItem) => void;
	} = $props();

	let preview = $state<LinkPreview | null>(null);
	let busy = $state(false);
	let error = $state<string | null>(null);
	/** Something worth saying that is not a problem. */
	let notice = $state<string | null>(null);


	const isUrl = $derived(looksLikeUrl(query));

	/**
	 * Whether adding is on the table at all.
	 *
	 * This used to also require resultCount === 0, which coupled two unrelated jobs. Paste a URL
	 * that happens to be a substring of something already here and the button silently
	 * disappeared, with nothing to say why you could not add the thing you had just pasted. It
	 * also raced: the count updates as the list filters, so Enter pressed straight after a paste
	 * did nothing at all.
	 *
	 * The count is still worth knowing — it is what tells somebody the link is already here —
	 * but it belongs in the message, not in whether the control exists.
	 */
	const showAdd = $derived(isOwner && isUrl);

	/** Already in this playlist, as far as the filtered list can tell. */
	const looksAlreadyHere = $derived(showAdd && resultCount > 0);

	let previewTimer: ReturnType<typeof setTimeout> | undefined;

	$effect(() => {
		const trimmed = query.trim();
		clearTimeout(previewTimer);
		// Nothing to preview for something already in the list — the row is right there.
		if (!showAdd || looksAlreadyHere || !trimmed) {
			preview = null;
			error = null;
			return;
		}
		previewTimer = setTimeout(doPreview, 700);
		return () => clearTimeout(previewTimer);
	});

	async function doPreview() {
		if (!showAdd || !query.trim() || busy) return;
		busy = true;
		error = null;
		preview = null;
		try {
			const res = await api.post('/links/preview', { url: query });
			if (!res.ok) {
				error = 'Could not preview that URL.';
				return;
			}
			preview = (await res.json()) as LinkPreview;
		} catch {
			error = 'Could not reach the server.';
		} finally {
			busy = false;
		}
	}

	async function doAdd() {
		if (!showAdd || !query.trim() || busy) return;
		busy = true;
		error = null;
		notice = null;
		try {
			const res = await api.post(`/playlists/${playlistId}/items`, { url: query });
			if (res.status === 409) {
				// Not a failure: the person wanted it here and it is here. Said plainly, and the
				// box is cleared so the list stops being filtered down to it.
				notice = 'That link is already in this playlist.';
				query = '';
				preview = null;
				return;
			}
			if (!res.ok) {
				error = 'Could not add the link.';
				return;
			}
			const item = (await res.json()) as PlaylistItem;
			query = '';
			preview = null;
			onAdded(item);
		} catch {
			error = 'Could not reach the server.';
		} finally {
			busy = false;
		}
	}
</script>

<div
	class="rounded-lg border p-3"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<div class="flex gap-2">
		<Input
			icon={Search}
			class="min-w-[12rem] flex-1"
			bind:value={query}
			placeholder={isOwner ? 'Search or paste a URL…' : 'Search…'}
			aria-label="Search or add link"
			onkeydown={(e) => e.key === 'Enter' && showAdd && doAdd()}
		/>
		{#if showAdd}
			<Button
				variant="primary"
				icon={Plus}
				onclick={doAdd}
				disabled={busy}
				aria-label={looksAlreadyHere ? 'Add it anyway' : 'Add link'}
			>
				{looksAlreadyHere ? 'Add anyway' : 'Add'}
			</Button>
		{/if}
	</div>

	{#if showAdd && preview}
		<div class="mt-3 flex gap-3 rounded-md border p-3" style="border-color: var(--color-border)">
			{#if preview.imageUrl}
				<img src={preview.imageUrl} alt="" class="h-14 w-14 rounded object-cover" />
			{/if}
			<div class="min-w-0">
				<div class="truncate font-medium">{preview.title ?? preview.canonicalUrl}</div>
				{#if preview.description}
					<div class="line-clamp-2 text-sm" style="color: var(--color-muted)">
						{preview.description}
					</div>
				{/if}
				<div class="text-xs" style="color: var(--color-muted)">{preview.host}</div>
			</div>
		</div>
	{/if}

	{#if looksAlreadyHere && !error}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			Something matching that is already in this playlist — it is in the list below.
		</p>
	{/if}

	{#if notice}
		<p class="mt-2 text-sm" style="color: var(--color-muted)" role="status">{notice}</p>
	{/if}

	{#if error}
		<p class="mt-2 text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
	{/if}
</div>
