<script lang="ts">
	import { api } from '$lib/api/client';
	import { ChevronDown, Globe, Lock, Play } from '@lucide/svelte';
	import SourceListItem from './SourceListItem.svelte';
	import type { Source } from '$lib/types';

	const PAGE_SIZE = 10;

	let { sources: initial }: { sources: Source[] } = $props();
	let sources = $state(initial);
	let visibleCount = $state(PAGE_SIZE);
	let toast = $state<string | null>(null);

	let visibleSources = $derived(sources.slice(0, visibleCount));
	let hasMore = $derived(sources.length > visibleCount);

	function loadMore() {
		visibleCount += PAGE_SIZE;
	}

	function displayType(type: string) {
		return type === 'Rss' ? 'RSS' : type;
	}

	async function run(src: Source) {
		const res = await api.post(`/sources/${src.id}/run`);
		toast = res.status === 202 ? `Queued a run for "${src.name}".` : `Could not run "${src.name}".`;
		setTimeout(() => (toast = null), 3000);
	}

	function lastRun(iso: string | null): string {
		if (!iso) return 'never';
		const diff = Date.now() - new Date(iso).getTime();
		const mins = Math.floor(diff / 60000);
		if (mins < 1) return 'just now';
		if (mins < 60) return `${mins}m ago`;
		const hours = Math.floor(mins / 60);
		if (hours < 24) return `${hours}h ago`;
		const days = Math.floor(hours / 24);
		if (days < 30) return `${days}d ago`;
		const months = Math.floor(days / 30);
		return months < 12 ? `${months}mo ago` : `${Math.floor(months / 12)}y ago`;
	}

	function statusColor(src: Source): string {
		if (!src.lastRunStatus) return '#9ca3af';
		if (src.lastRunStatus === 'Failed') return '#ef4444';
		if (src.lastRunStatus === 'Running') return '#f59e0b';
		return '#22c55e';
	}

	function statusLabel(src: Source): string {
		if (!src.lastRunStatus) return 'Never run';
		if (src.lastRunStatus === 'Failed') return 'Last run failed';
		if (src.lastRunStatus === 'Running') return 'Currently running';
		return 'Last run succeeded';
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
		{#each visibleSources as src (src.id)}
			<SourceListItem
				name={src.name}
				badge={displayType(src.type)}
				href={`/sources/${src.id}`}
				subtitle={`last run ${lastRun(src.lastRunAt)}`}
			>
				{#snippet leading()}
					<span
						style="width: 8px; height: 8px; border-radius: 50%; background: {statusColor(src)}; display: block; flex-shrink: 0;"
						title={statusLabel(src)}
						aria-label={statusLabel(src)}
					></span>
				{/snippet}
				{#snippet actions()}
					<span title={src.visibility} aria-label={src.visibility} style="color: var(--color-muted)">
						{#if src.visibility === 'Private'}
							<Lock size={15} aria-hidden="true" />
						{:else}
							<Globe size={15} aria-hidden="true" />
						{/if}
					</span>
					<button type="button" onclick={() => run(src)} class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10" title="Run now" aria-label={`Run ${src.name} now`}>
						<Play size={15} aria-hidden="true" />
					</button>
				{/snippet}
			</SourceListItem>
		{/each}
	</ul>
	{#if hasMore}
		<button
			type="button"
			onclick={loadMore}
			class="mt-2 flex w-full items-center justify-center gap-1.5 rounded-lg border p-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
			style="border-color: var(--color-border); color: var(--color-muted)"
		>
			Load more
			<ChevronDown size={15} aria-hidden="true" />
		</button>
	{/if}
{/if}

{#if toast}
	<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
{/if}
