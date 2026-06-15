<script lang="ts">
	import { api } from '$lib/api/client';
	import { Plus } from '@lucide/svelte';
	import type { LinkPreview, PlaylistItem } from '$lib/types';

	let { playlistId, onAdded }: { playlistId: string; onAdded: (item: PlaylistItem) => void } =
		$props();

	let url = $state('');
	let preview = $state<LinkPreview | null>(null);
	let busy = $state(false);
	let error = $state<string | null>(null);

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	let previewTimer: ReturnType<typeof setTimeout> | undefined;

	$effect(() => {
		const trimmed = url.trim();
		clearTimeout(previewTimer);
		if (!trimmed) {
			preview = null;
			error = null;
			return;
		}
		previewTimer = setTimeout(doPreview, 700);
		return () => clearTimeout(previewTimer);
	});

	async function doPreview() {
		if (!url.trim() || busy) return;
		busy = true;
		error = null;
		preview = null;
		try {
			const res = await api.post('/links/preview', { url });
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
		if (!url.trim() || busy) return;
		busy = true;
		error = null;
		try {
			const res = await api.post(`/playlists/${playlistId}/items`, { url });
			if (res.status === 409) {
				error = 'That link is already in this playlist.';
				return;
			}
			if (!res.ok) {
				error = 'Could not add the link.';
				return;
			}
			onAdded((await res.json()) as PlaylistItem);
			url = '';
			preview = null;
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
		<input
			bind:value={url}
			placeholder="Paste a URL…"
			aria-label="Link URL"
			class="{fieldClass} flex-1 min-w-[12rem]"
			style={fieldStyle}
			onkeydown={(e) => e.key === 'Enter' && doAdd()}
		/>
		<button
			type="button"
			onclick={doAdd}
			disabled={busy}
			class="rounded-md p-2 disabled:opacity-60"
			style="background: var(--color-accent); color: var(--color-accent-contrast)"
			title="Add link"
			aria-label="Add link"
		>
			<Plus size={16} aria-hidden="true" />
		</button>
	</div>

	{#if preview}
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
