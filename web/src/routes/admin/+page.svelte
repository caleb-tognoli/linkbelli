<script lang="ts">
	import { AlertCircle, ExternalLink } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const o = $derived(data.overview);

	/** The headline numbers, in the order someone actually scans them. */
	const totals = $derived([
		{ label: 'Users', value: o.users },
		{ label: 'Playlists', value: o.playlists },
		{ label: 'Links', value: o.links },
		{ label: 'Items', value: o.items },
		{ label: 'Sources', value: o.sources }
	]);

	// Separated from the totals because a number here is something to do, not something to know.
	const attention = $derived([
		{ label: 'Waiting to be fetched', value: o.pendingEnrichment, bad: o.pendingEnrichment > 500 },
		{ label: 'Broken links', value: o.brokenLinks, bad: o.brokenLinks > 0 },
		{ label: 'Failing sources', value: o.failingSources, bad: o.failingSources > 0 },
		{
			label: `Failed runs (${o.recentDays}d)`,
			value: o.failedRunsRecently,
			bad: o.failedRunsRecently > 0
		}
	]);

	function when(iso: string | null): string {
		return iso ? new Date(iso).toLocaleString() : 'never';
	}
</script>

<svelte:head><title>Admin - linkbelli</title></svelte:head>

<section class="mx-auto max-w-4xl">
	<h1 class="text-2xl font-semibold">Instance</h1>
	<p class="mt-1 text-sm" style="color: var(--color-muted)">
		Everything below was already being recorded. This is where it can be seen.
	</p>

	<dl class="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-5">
		{#each totals as stat (stat.label)}
			<div class="rounded-lg border px-3 py-2" style="border-color: var(--color-border); background: var(--color-surface)">
				<dt class="text-xs" style="color: var(--color-muted)">{stat.label}</dt>
				<dd class="text-lg tabular-nums">{stat.value.toLocaleString()}</dd>
			</div>
		{/each}
	</dl>

	<h2 class="mt-8 font-medium">Worth a look</h2>
	<dl class="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
		{#each attention as stat (stat.label)}
			<div class="rounded-lg border px-3 py-2" style="border-color: var(--color-border); background: var(--color-surface)">
				<dt class="text-xs" style="color: var(--color-muted)">{stat.label}</dt>
				<dd
					class="text-lg tabular-nums"
					style={stat.bad ? 'color: var(--color-danger)' : ''}
				>{stat.value.toLocaleString()}</dd>
			</div>
		{/each}
	</dl>

	<h2 class="mt-8 font-medium">Background jobs</h2>
	{#if o.jobs}
		{@const jobs = o.jobs}
		<dl class="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-5">
			{#each [{ label: 'Enqueued', value: jobs.enqueued }, { label: 'Processing', value: jobs.processing }, { label: 'Scheduled', value: jobs.scheduled }, { label: 'Failed', value: jobs.failed }, { label: 'Succeeded', value: jobs.succeeded }] as stat (stat.label)}
				<div class="rounded-lg border px-3 py-2" style="border-color: var(--color-border); background: var(--color-surface)">
					<dt class="text-xs" style="color: var(--color-muted)">{stat.label}</dt>
					<dd
						class="text-lg tabular-nums"
						style={stat.label === 'Failed' && stat.value > 0 ? 'color: var(--color-danger)' : ''}
					>{stat.value.toLocaleString()}</dd>
				</div>
			{/each}
		</dl>
	{:else}
		<!-- Not an empty state: a console that hides the runner being unreachable is worse than
		     no console. -->
		<p class="mt-3 flex items-center gap-2 text-sm" style="color: var(--color-danger)">
			<AlertCircle size={15} aria-hidden="true" />
			Could not reach the job runner.
		</p>
	{/if}

	<h2 class="mt-8 font-medium">Sources needing attention</h2>
	{#if o.topFailingSources.length === 0}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">None failing.</p>
	{:else}
		<div class="mt-3 overflow-x-auto">
			<table class="w-full border-collapse text-sm">
				<thead>
					<tr class="text-left" style="color: var(--color-muted)">
						<th class="py-1 font-medium">Source</th>
						<th class="py-1 font-medium">Owner</th>
						<th class="py-1 font-medium">Failures</th>
						<th class="py-1 font-medium">Last run</th>
						<th class="py-1 font-medium">Error</th>
					</tr>
				</thead>
				<tbody>
					{#each o.topFailingSources as source (source.id)}
						<tr class="border-t align-top" style="border-color: var(--color-border)">
							<td class="py-1.5">{source.name}</td>
							<td class="py-1.5" style="color: var(--color-muted)">@{source.ownerUsername}</td>
							<td class="py-1.5 tabular-nums" style={source.status === 'Failing' ? 'color: var(--color-danger)' : ''}>
								{source.consecutiveFailures}
							</td>
							<td class="py-1.5 whitespace-nowrap" style="color: var(--color-muted)">{when(source.lastRunAt)}</td>
							<td class="py-1.5" style="color: var(--color-muted)">{source.lastError ?? '—'}</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}

	<h2 class="mt-8 font-medium">Where the links live</h2>
	{#if o.topHosts.length === 0}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">Nothing saved yet.</p>
	{:else}
		<ul class="mt-3 flex flex-col gap-1 text-sm">
			{#each o.topHosts as host (host.hostname)}
				<li class="flex items-baseline justify-between gap-3 border-t py-1.5 first:border-t-0" style="border-color: var(--color-border)">
					<span class="truncate">{host.hostname}</span>
					<span class="shrink-0 tabular-nums" style="color: var(--color-muted)">
						{host.linkCount.toLocaleString()}
						{#if host.failedCount > 0}
							· <span style="color: var(--color-danger)">{host.failedCount} unreadable</span>
						{/if}
					</span>
				</li>
			{/each}
		</ul>
	{/if}

	<h2 class="mt-8 font-medium">Recent fetch errors</h2>
	{#if o.recentErrors.length === 0}
		<p class="mt-2 pb-10 text-sm" style="color: var(--color-muted)">
			Nothing has failed in the last {o.recentDays} days.
		</p>
	{:else}
		<ul class="mt-3 flex flex-col gap-2 pb-10 text-sm">
			{#each o.recentErrors as row (row.linkId)}
				<li class="border-t pt-2 first:border-t-0" style="border-color: var(--color-border)">
					<a
						href={row.url}
						target="_blank"
						rel="noreferrer"
						class="inline-flex items-center gap-1 break-all hover:underline"
					>
						{row.url}
						<ExternalLink size={11} aria-hidden="true" class="shrink-0" />
					</a>
					<p class="mt-0.5 text-xs" style="color: var(--color-muted)">
						{row.status} · {row.error} · {row.failureCount}
						{row.failureCount === 1 ? 'attempt' : 'attempts'} · {when(row.lastCheckedAt)}
					</p>
				</li>
			{/each}
		</ul>
	{/if}
</section>
