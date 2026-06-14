<script lang="ts">
	import { api } from '$lib/api/client';
	import SourceListItem from './SourceListItem.svelte';
	import type { Source } from '$lib/types';

	let { sources: initial }: { sources: Source[] } = $props();
	let sources = $state(initial);
	let toast = $state<string | null>(null);

	async function toggle(src: Source) {
		const next = !src.enabled;
		const res = await api.patch(`/sources/${src.id}`, { enabled: next });
		if (res.ok) sources = sources.map((s) => (s.id === src.id ? { ...s, enabled: next } : s));
	}

	async function run(src: Source) {
		const res = await api.post(`/sources/${src.id}/run`);
		toast = res.status === 202 ? `Queued a run for "${src.name}".` : `Could not run "${src.name}".`;
		setTimeout(() => (toast = null), 3000);
	}

	function lastRun(iso: string | null) {
		return iso ? new Date(iso).toLocaleString() : 'never';
	}
</script>

{#if sources.length === 0}
	<div class="rounded-lg border border-dashed p-8 text-center" style="border-color: var(--color-border)">
		<p class="font-medium">No sources yet.</p>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Sources automatically pull links (RSS, scrapers, JSON APIs) into your playlists.
		</p>
	</div>
{:else}
	<ul class="flex flex-col gap-2">
		{#each sources as src (src.id)}
			<SourceListItem
				name={src.name}
				href={`/sources/${src.id}`}
				subtitle={`${src.type} · ${src.schedule} · ${src.visibility}${src.nsfw ? ' · NSFW' : ''} · last run ${lastRun(src.lastRunAt)}`}
			>
				{#snippet actions()}
					<label class="flex items-center gap-1 text-xs" style="color: var(--color-muted)">
						<input type="checkbox" checked={src.enabled} onchange={() => toggle(src)} /> enabled
					</label>
					<button type="button" onclick={() => run(src)} class="rounded border px-2 py-1 text-xs" style="border-color: var(--color-border)">
						Run now
					</button>
				{/snippet}
			</SourceListItem>
		{/each}
	</ul>
{/if}

{#if toast}
	<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
{/if}
