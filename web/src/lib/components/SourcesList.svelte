<script lang="ts">
	import Button from '$lib/components/ui/Button.svelte';
	import { api } from '$lib/api/client';
	import { AlertTriangle, ChevronDown, Globe, Lock, Play, Plus, Search } from '@lucide/svelte';
	import SourceListItem from './SourceListItem.svelte';
	import type { Source } from '$lib/types';

	const PAGE_SIZE = 10;

	let { sources: initial }: { sources: Source[] } = $props();
	let sources = $state(initial);
	let visibleCount = $state(PAGE_SIZE);
	let toast = $state<string | null>(null);
	let query = $state('');

	// Own sources arrive as one full array (GET /sources takes no query), so filter client-side.
	// Matching name, type and target URL covers how you actually look a source up.
	const filtered = $derived.by(() => {
		const q = query.trim().toLowerCase();
		if (!q) return sources;
		return sources.filter(
			(s) =>
				s.name.toLowerCase().includes(q) ||
				displayType(s.type).toLowerCase().includes(q) ||
				(s.config?.url ?? '').toLowerCase().includes(q)
		);
	});

	let visibleSources = $derived(filtered.slice(0, visibleCount));
	let hasMore = $derived(filtered.length > visibleCount);

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

	// Theme tokens rather than fixed hex: the old greys, reds and greens were the same in both
	// themes and between 2.0:1 and 2.4:1 against a card — too faint for a dot that carries
	// meaning, which needs 3:1.
	function statusColor(src: Source): string {
		if (!src.lastRunStatus) return 'var(--color-muted)';
		if (src.lastRunStatus === 'Failed') return 'var(--color-danger)';
		if (src.lastRunStatus === 'Running') return 'var(--color-warning)';
		return 'var(--color-success)';
	}

	function statusLabel(src: Source): string {
		if (!src.lastRunStatus) return 'Never run';
		if (src.lastRunStatus === 'Failed') return 'Last run failed';
		if (src.lastRunStatus === 'Running') return 'Currently running';
		return 'Last run succeeded';
	}

	// A stopped source shows a hollow ring rather than a filled dot: the fill means "this is
	// running on a schedule", which is exactly what stopping ends. A source that stopped itself
	// after repeated failures reads as a problem, not a choice.
	const stopped = (src: Source) => src.status !== 'Active';
	const failing = (src: Source) => src.status === 'Failing';

	function dotStyle(src: Source): string {
		const base = 'width: 10px; height: 10px; border-radius: 50%; display: block; flex-shrink: 0;';
		if (failing(src)) return `${base} border: 1.5px solid var(--color-danger);`;
		if (stopped(src)) return `${base} border: 1.5px solid var(--color-muted);`;
		return `${base} background: ${statusColor(src)};`;
	}

	function dotLabel(src: Source): string {
		if (failing(src)) return `Stopped after ${src.consecutiveFailures} failures`;
		if (stopped(src)) return `Paused — ${statusLabel(src).toLowerCase()}`;
		return statusLabel(src);
	}
</script>

{#if sources.length === 0}
	<div class="rounded-lg border border-dashed p-8 text-center" style="border-color: var(--color-border)">
		<p class="font-medium">No sources yet.</p>
		<p class="mx-auto mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
			A source watches something — a feed, a subreddit, a channel, a page — and drops what it
			finds straight into a playlist, on a schedule you set. It is the difference between a
			list you fill and one that fills itself.
		</p>
		<Button href="/sources/new" variant="primary" icon={Plus} class="mt-4">Add a source</Button>
		<p class="mt-3 text-xs" style="color: var(--color-muted)">
			There are ready-made ones for YouTube, Reddit, Hacker News, GitHub and podcasts.
		</p>
	</div>
{:else}
	<div class="relative">
		<Search
			size={15}
			aria-hidden="true"
			class="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
			style="color: var(--color-muted)"
		/>
		<input
			bind:value={query}
			oninput={() => (visibleCount = PAGE_SIZE)}
			placeholder="Search sources…"
			aria-label="Search sources"
			class="w-full rounded-md border py-2 pr-3 pl-9 text-sm"
			style="border-color: var(--color-border-strong); background: var(--color-bg)"
		/>
	</div>

	{#if filtered.length === 0}
		<div class="mt-3 rounded-lg border border-dashed p-8 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">No matching sources.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">Try a different name, type, or URL.</p>
		</div>
	{:else}
		<ul class="mt-3 flex flex-col gap-2">
			{#each visibleSources as src (src.id)}
				<SourceListItem
					name={src.name}
					badge={displayType(src.type)}
					href={`/sources/${src.id}`}
					subtitle={failing(src)
						? `stopped after ${src.consecutiveFailures} failures · last run ${lastRun(src.lastRunAt)}`
						: stopped(src)
							? `paused · last run ${lastRun(src.lastRunAt)}`
							: src.quiet
								? `running, but found nothing this week · last run ${lastRun(src.lastRunAt)}`
								: `last run ${lastRun(src.lastRunAt)}`}
				>
					{#snippet leading()}
						<span
							style={dotStyle(src)}
							title={dotLabel(src)}
							aria-label={dotLabel(src)}
						></span>
					{/snippet}
					{#snippet actions()}
						{#if src.quiet}
							<!-- The failure nobody reports. A scraper whose selector stopped matching
							     succeeds every single time, so the only symptom is a playlist that
							     stopped filling — which you notice weeks later, if at all. -->
							<a
								href={`/sources/${src.id}`}
								style="color: var(--color-warning)"
								title="Running cleanly and finding nothing — worth a look"
								aria-label="{src.name} is running but finding nothing"
							>
								<AlertTriangle size={15} aria-hidden="true" />
							</a>
						{/if}
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
{/if}

{#if toast}
	<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
{/if}
