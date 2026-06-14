<script lang="ts">
	import { api } from '$lib/api/client';
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

	<ul class="mt-3 flex flex-col gap-1">
		{#each attached as src (src.id)}
			<li class="flex items-center justify-between gap-2 text-sm">
				<span class="truncate">
					{src.name}
					<span class="text-xs" style="color: var(--color-muted)">
						· {src.type}{src.ownedByMe ? '' : ` · @${src.ownerUsername}`}
					</span>
				</span>
				<button
					type="button"
					onclick={() => detach(src.id)}
					disabled={busy}
					class="text-xs hover:underline"
					style="color: var(--color-danger)"
				>
					Detach
				</button>
			</li>
		{:else}
			<li class="text-sm" style="color: var(--color-muted)">No sources attached.</li>
		{/each}
	</ul>

	{#if ownUnattached.length}
		<div class="mt-4">
			<div class="text-xs font-medium" style="color: var(--color-muted)">Attach one of yours</div>
			<ul class="mt-1 flex flex-col gap-1">
				{#each ownUnattached as src (src.id)}
					<li class="flex items-center justify-between gap-2 text-sm">
						<span class="truncate">{src.name} <span class="text-xs" style="color: var(--color-muted)">· {src.type}</span></span>
						<button type="button" onclick={() => subscribe(src.id)} disabled={busy} class="text-xs hover:underline" style="color: var(--color-accent)">
							Attach
						</button>
					</li>
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
				class="flex-1 rounded border px-2 py-1 text-sm"
				style="border-color: var(--color-border); background: var(--color-bg)"
				onkeydown={(e) => e.key === 'Enter' && searchShared()}
			/>
			<button type="button" onclick={searchShared} disabled={searching} class="rounded border px-2 py-1 text-sm" style="border-color: var(--color-border)">
				Search
			</button>
		</div>
		{#if sharedResults.length}
			<ul class="mt-1 flex flex-col gap-1">
				{#each sharedResults as src (src.id)}
					<li class="flex items-center justify-between gap-2 text-sm">
						<span class="truncate">{src.name} <span class="text-xs" style="color: var(--color-muted)">· {src.type} · @{src.ownerUsername}</span></span>
						<button type="button" onclick={() => subscribe(src.id)} disabled={busy} class="text-xs hover:underline" style="color: var(--color-accent)">
							Subscribe
						</button>
					</li>
				{/each}
			</ul>
		{/if}
	</div>
</div>
