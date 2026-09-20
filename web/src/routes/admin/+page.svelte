<script lang="ts">
	import { formatDate, formatExact } from '$lib/dates';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import { confirmDialog } from '$lib/dialog.svelte';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import Badge from '$lib/components/ui/Badge.svelte';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { AlertCircle, ExternalLink } from '@lucide/svelte';
	import { page } from '$app/state';
	import AdminUsersPanel from '$lib/components/AdminUsersPanel.svelte';
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
		return iso ? formatDate(iso) : 'never';
	}

	const openReports = $derived(data.reports.filter((r) => r.status === 'Open'));

	let busy = $state<string | null>(null);

	async function resolve(id: string, action: { dismiss?: boolean; takeDown?: boolean }) {
		if (action.takeDown) {
			const report = data.reports.find((r) => r.id === id);
			const ok = await confirmDialog(`Take down "${report?.playlistName ?? 'this playlist'}"?`, {
				description: 'It becomes private. Its owner keeps it and is not told why.',
				danger: true,
				confirmLabel: 'Take it down'
			});
			if (!ok) return;
		}

		busy = id;
		try {
			const res = await api.post(`/admin/reports/${id}/resolve`, action);
			if (res.ok) {
				toast.success(action.takeDown ? 'Taken down: it is private now.' : 'Report dismissed.');
				await invalidateAll();
			} else {
				toast.error(failureMessage(res.status, 'Could not resolve that report.'));
			}
		} finally {
			busy = null;
		}
	}
</script>

<svelte:head><title>Admin - linkbelli</title></svelte:head>

<Page width="medium">
	<PageHeader title="Instance" description="Everything below was already being recorded. This is where it can be seen." />

	<dl class="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-5">
		{#each totals as stat (stat.label)}
			<div class="rounded-lg border px-3 py-2" style="border-color: var(--color-border); background: var(--color-surface)">
				<dt class="text-xs" style="color: var(--color-muted)">{stat.label}</dt>
				<dd class="text-lg tabular-nums">{stat.value.toLocaleString()}</dd>
			</div>
		{/each}
	</dl>

	<h2 class="mt-8 t-section">Worth a look</h2>
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

	<h2 class="mt-8 t-section">Background jobs</h2>
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

	<h2 class="mt-8 t-section">Sources needing attention</h2>
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

	<h2 class="mt-8 t-section">Where the links live</h2>
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

	<h2 class="mt-8 t-section">
		Reports
		{#if openReports.length}
			<span class="ml-1 text-sm" style="color: var(--color-danger)">{openReports.length} open</span>
		{/if}
	</h2>
	{#if data.reports.length === 0}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">Nothing reported.</p>
	{:else}
		<ul class="mt-3 flex flex-col text-sm">
			{#each data.reports as report (report.id)}
				<li class="border-t py-3 first:border-t-0" style="border-color: var(--color-border)">
					<div class="flex flex-wrap items-baseline justify-between gap-2">
						<a
							href={`/public/${encodeURIComponent(report.ownerUsername)}/${encodeURIComponent(report.playlistSlug)}`}
							class="font-medium hover:underline"
						>{report.playlistName}</a>
						<span class="text-xs" style="color: var(--color-muted)">
							{report.reason} · reported by @{report.reportedBy} ·
							{new Date(report.reportedAt).toLocaleDateString()}
						</span>
					</div>

					{#if report.note}
						<p class="mt-1" style="color: var(--color-muted)">{report.note}</p>
					{/if}

					{#if report.status === 'Open'}
						<div class="mt-2 flex flex-wrap items-center gap-2">
							<button
								type="button"
								onclick={() => resolve(report.id, { dismiss: true })}
								disabled={busy === report.id}
								class="rounded-md border px-2.5 py-1 text-xs disabled:opacity-60"
								style="border-color: var(--color-border)"
							>Dismiss report</button>
							<!-- Private, not deleted: the owner keeps their work, and it stops being
							     published. Deleting a collection over a report is not recoverable. -->
							<button
								type="button"
								onclick={() => resolve(report.id, { takeDown: true })}
								disabled={busy === report.id}
								class="rounded-md border px-2.5 py-1 text-xs disabled:opacity-60"
								style="border-color: var(--color-danger); color: var(--color-danger)"
							>Take it down</button>
						</div>
					{:else}
						<p class="mt-1 text-xs" style="color: var(--color-muted)">
							{report.status}{report.resolution ? ` · ${report.resolution}` : ''}
							{#if report.visibility === 'Private'} · now private{/if}
						</p>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}

	<AdminUsersPanel me={page.data.user?.username ?? null} />

	<h2 class="mt-8 t-section">Recent actions</h2>
	{#if data.audit.length === 0}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">Nothing recorded yet.</p>
	{:else}
		<ul class="mt-3 flex flex-col text-sm">
			{#each data.audit as entry (entry.id)}
				<li class="border-t py-2 first:border-t-0" style="border-color: var(--color-border)">
					<div class="flex flex-wrap items-baseline gap-x-2">
						<span class="font-medium">@{entry.actorName}</span>
						{#if entry.asAdmin}
							<!-- Reaching into other people's data is the part worth marking. -->
							<Badge tone="danger">admin</Badge>
						{/if}
						<span style="color: var(--color-muted)">{entry.summary ?? entry.action}</span>
					</div>
					<p class="mt-0.5 text-xs" style="color: var(--color-muted)">
						{entry.action} · <time datetime={entry.at} title={formatExact(entry.at)}>{formatDate(entry.at)}</time>
					</p>
					{#if entry.details}
						<details class="mt-1">
							<summary class="cursor-pointer text-xs" style="color: var(--color-muted)">Details</summary>
							<pre
								class="mt-1 overflow-x-auto rounded p-2 text-xs"
								style="background: var(--color-bg)">{entry.details}</pre>
						</details>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}

	<h2 class="mt-8 t-section">Recent fetch errors</h2>
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
</Page>
