<script lang="ts">
	import SkeletonRows from '$lib/components/ui/SkeletonRows.svelte';
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
	import Modal from '$lib/components/ui/Modal.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import { EyeOff, Globe, Lock } from '@lucide/svelte';
	import { api, json } from '$lib/api/client';
	import type { Paged, Playlist } from '$lib/types';

	const visIcons = { Private: Lock, Unlisted: EyeOff, Public: Globe } as const;

	let {
		open = $bindable(false),
		title,
		subtitle = undefined,
		excludePlaylistId = undefined,
		onselect
	}: {
		open: boolean;
		title: string;
		subtitle?: string;
		excludePlaylistId?: string;
		/** Return a label to show in the row (null/undefined → 'Added'). Throw to leave the row unchanged. */
		onselect: (playlistId: string) => Promise<string | null | undefined>;
	} = $props();

	let playlists = $state<Playlist[]>([]);
	let loading = $state(false);
	let search = $state('');
	let nextCursor = $state<string | null>(null);
	let rowStatus = $state<Map<string, string>>(new Map());

	async function fetchPage(reset: boolean, cursor?: string) {
		loading = true;
		try {
			const params = new URLSearchParams({ limit: '10' });
			if (search.trim()) params.set('q', search.trim());
			if (!reset && cursor) params.set('cursor', cursor);
			const res = await api.get(`/playlists?${params}`);
			if (!res.ok) return;
			const paged = await json<Paged<Playlist>>(res);
			const items = excludePlaylistId
				? paged.items.filter((p) => p.id !== excludePlaylistId)
				: paged.items;
			playlists = reset ? items : [...playlists, ...items];
			nextCursor = paged.nextCursor;
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		if (!open) return;
		rowStatus = new Map();
		playlists = [];
		nextCursor = null;
		search = '';
	});

	$effect(() => {
		if (!open) return;
		const term = search;
		const delay = term ? 300 : 0;
		const t = setTimeout(() => fetchPage(true), delay);
		return () => clearTimeout(t);
	});

	async function pick(pl: Playlist) {
		if (rowStatus.has(pl.id)) return;
		try {
			const label = await onselect(pl.id);
			rowStatus = new Map(rowStatus).set(pl.id, label ?? 'Added');
		} catch {
			// leave row unchanged
		}
	}
</script>

<Modal bind:open {title} description={subtitle} size="sm">
	<Input
		bind:value={search}
		placeholder="Search…"
		aria-label="Search playlists"
		size="sm"
		class="w-full"
	/>

	<div class="mt-2 flex-1 overflow-y-auto">
		{#if loading && playlists.length === 0}
			<SkeletonRows rows={6} class="py-2" />
		{:else if playlists.length === 0}
			<p class="py-2 text-sm" style="color: var(--color-muted)">{search.trim() ? 'No matches.' : 'No playlists found.'}</p>
		{:else}
			<ul class="flex flex-col gap-1">
				{#each playlists as pl (pl.id)}
					{@const status = rowStatus.get(pl.id)}
					{@const VisIcon = visIcons[pl.visibility] ?? Lock}
					<li>
						<button
							type="button"
							onclick={() => pick(pl)}
							disabled={!!status}
							class="flex w-full items-center justify-between rounded-md px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-60"
						>
							<span class="truncate">{pl.name}</span>
							<span class="ml-2 flex shrink-0 items-center gap-1.5">
								{#if status}
									<span class="text-xs font-medium" style="color: var(--color-accent)">{status}</span>
								{/if}
								<VisIcon size={13} aria-label={pl.visibility} style="color: var(--color-muted)" />
							</span>
						</button>
					</li>
				{/each}
			</ul>
			{#if nextCursor}
				<LoadMore onclick={() => fetchPage(false, nextCursor ?? undefined)} {loading} />
			{/if}
		{/if}
	</div>
</Modal>
