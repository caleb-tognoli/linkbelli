<script lang="ts">
	import { api } from '$lib/api/client';
	import { Globe, Lock, Play } from '@lucide/svelte';
	import SourceListItem from './SourceListItem.svelte';
	import type { Source } from '$lib/types';

	let { sources: initial }: { sources: Source[] } = $props();
	let sources = $state(initial);
	let toast = $state<string | null>(null);

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
				badge={displayType(src.type)}
				href={`/sources/${src.id}`}
				subtitle={`last run ${lastRun(src.lastRunAt)}`}
			>
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
{/if}

{#if toast}
	<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
{/if}
