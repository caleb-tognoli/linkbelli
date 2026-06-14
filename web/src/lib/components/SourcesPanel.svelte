<script lang="ts">
	import { api } from '$lib/api/client';
	import SourceListItem from './SourceListItem.svelte';
	import type { AttachedSource, SharedSource, SourceSummary } from '$lib/types';

	let {
		playlistId,
		attached = $bindable(),
		ownSources
	}: { playlistId: string; attached: AttachedSource[]; ownSources: SourceSummary[] } = $props();

	let query = $state('');
	let sharedResults = $state<SharedSource[]>([]);
	let searching = $state(false);
	let busy = $state(false);

	const attachedIds = $derived(new Set(attached.map((s) => s.id)));
	const ownUnattached = $derived(ownSources.filter((s) => !attachedIds.has(s.id)));

	async function refresh() {
		const res = await api.get(`/playlists/${playlistId}/sources`);
		if (res.ok) attached = (await res.json()) as AttachedSource[];
	}

	async function subscribe(sourceId: string) {
		busy = true;
		try {
			const res = await api.post(`/playlists/${playlistId}/sources`, { sourceId });
			if (res.ok || res.status === 204) await refresh();
		} finally {
			busy = false;
		}
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
		searching = true;
		try {
			const res = await api.get(`/sources/shared?q=${encodeURIComponent(query.trim())}`);
			if (res.ok) sharedResults = ((await res.json()) as SharedSource[]).filter((s) => !attachedIds.has(s.id));
		} finally {
			searching = false;
		}
	}
</script>

<div class="rounded-lg border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
	<h2 class="font-medium">Sources</h2>
	<p class="mt-0.5 text-xs" style="color: var(--color-muted)">
		Sources feed new links into this playlist automatically.
	</p>

	{#if attached.length}
		<ul class="mt-3 flex flex-col gap-2">
			{#each attached as src (src.id)}
				<SourceListItem
					name={src.name}
					href={src.ownedByMe ? `/sources/${src.id}` : undefined}
					subtitle={`${src.type}${src.ownedByMe ? '' : ` · @${src.ownerUsername}`}`}
				>
					{#snippet actions()}
						<button type="button" onclick={() => detach(src.id)} disabled={busy} class="text-xs hover:underline" style="color: var(--color-danger)">
							Unsubscribe
						</button>
					{/snippet}
				</SourceListItem>
			{/each}
		</ul>
	{:else}
		<p class="mt-3 text-sm" style="color: var(--color-muted)">No sources attached.</p>
	{/if}

	{#if ownUnattached.length}
		<div class="mt-4">
			<div class="text-xs font-medium" style="color: var(--color-muted)">Attach one of yours</div>
			<ul class="mt-2 flex flex-col gap-2">
				{#each ownUnattached as src (src.id)}
					<SourceListItem name={src.name} href={`/sources/${src.id}`} subtitle={src.type}>
						{#snippet actions()}
							<button type="button" onclick={() => subscribe(src.id)} disabled={busy} class="text-xs hover:underline" style="color: var(--color-accent)">
								Attach
							</button>
						{/snippet}
					</SourceListItem>
				{/each}
			</ul>
		</div>
	{/if}

	<div class="mt-4">
		<div class="text-xs font-medium" style="color: var(--color-muted)">Subscribe to a shared source</div>
		<div class="mt-1 flex gap-2">
			<input
				bind:value={query}
				placeholder="search shared…"
				aria-label="Search shared sources"
				class="flex-1 rounded border px-2 py-1 text-sm"
				style="border-color: var(--color-border); background: var(--color-bg)"
				onkeydown={(e) => e.key === 'Enter' && searchShared()}
			/>
			<button type="button" onclick={searchShared} disabled={searching} class="rounded border px-2 py-1 text-sm" style="border-color: var(--color-border)">
				Search
			</button>
		</div>
		{#if sharedResults.length}
			<ul class="mt-2 flex flex-col gap-2">
				{#each sharedResults as src (src.id)}
					<SourceListItem name={src.name} subtitle={`${src.type} · @${src.ownerUsername}`}>
						{#snippet actions()}
							<button type="button" onclick={() => subscribe(src.id)} disabled={busy} class="text-xs hover:underline" style="color: var(--color-accent)">
								Subscribe
							</button>
						{/snippet}
					</SourceListItem>
				{/each}
			</ul>
		{/if}
	</div>
</div>
