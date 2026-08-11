<script lang="ts">
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

	const fieldClass = 'rounded-md border pl-9 pr-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	const isUrl = $derived(looksLikeUrl(query));
	const showAdd = $derived(isOwner && isUrl && resultCount === 0);

	let previewTimer: ReturnType<typeof setTimeout> | undefined;

	$effect(() => {
		const trimmed = query.trim();
		clearTimeout(previewTimer);
		if (!showAdd || !trimmed) {
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
		try {
			const res = await api.post(`/playlists/${playlistId}/items`, { url: query });
			if (res.status === 409) {
				error = 'That link is already in this playlist.';
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
		<div class="relative flex-1 min-w-[12rem]">
			<Search
				size={15}
				aria-hidden="true"
				class="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
				style="color: var(--color-muted)"
			/>
			<input
				bind:value={query}
				placeholder={isOwner ? 'Search or paste a URL…' : 'Search…'}
				aria-label="Search or add link"
				class="{fieldClass} w-full"
				style={fieldStyle}
				onkeydown={(e) => e.key === 'Enter' && showAdd && doAdd()}
			/>
		</div>
		{#if showAdd}
			<button
				type="button"
				onclick={doAdd}
				disabled={busy}
				class="rounded-md p-2 disabled:opacity-60"
				style="background: var(--color-accent); color: var(--color-accent-contrast)"
				title="Add link"
				aria-label="Add link"
			>
				<Plus size={18} aria-hidden="true" />
			</button>
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

	{#if error}
		<p class="mt-2 text-sm" style="color: var(--color-danger)">{error}</p>
	{/if}
</div>
