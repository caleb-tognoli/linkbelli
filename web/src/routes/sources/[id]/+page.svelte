<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import SourceForm from '$lib/components/SourceForm.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
	let busy = $state(false);
	let toast = $state<string | null>(null);

	async function runNow() {
		busy = true;
		try {
			const res = await api.post(`/sources/${data.source.id}/run`);
			toast = res.status === 202 ? 'Run queued. Refresh history in a moment.' : 'Could not queue a run.';
		} finally {
			busy = false;
		}
	}

	async function remove() {
		if (!confirm('Delete this source? Playlists keep their existing links.')) return;
		busy = true;
		const res = await api.del(`/sources/${data.source.id}`);
		if (res.ok || res.status === 204) await goto('/#sources');
		else busy = false;
	}

	function fmt(iso: string | null) {
		return iso ? new Date(iso).toLocaleString() : '—';
	}
</script>

<section class="mx-auto max-w-2xl">
	<a href="/#sources" class="text-sm" style="color: var(--color-muted)">← Sources</a>

	<header class="mt-3 flex items-center justify-between gap-3">
		<h1 class="text-2xl font-semibold">{data.source.name}</h1>
		<div class="flex shrink-0 gap-2 text-sm">
			<button type="button" onclick={runNow} disabled={busy} class="rounded-md border px-3 py-1.5" style="border-color: var(--color-border)">
				Run now
			</button>
			<button type="button" onclick={remove} disabled={busy} class="rounded-md border px-3 py-1.5" style="border-color: var(--color-border); color: var(--color-danger)">
				Delete
			</button>
		</div>
	</header>

	{#if toast}
		<p class="mt-2 text-sm" style="color: var(--color-muted)">{toast}</p>
	{/if}

	{#key data.source.id}
		<div class="mt-5">
			<SourceForm mode="edit" source={data.source} playlists={data.playlists} />
		</div>
	{/key}

	<div class="mt-8">
		<div class="flex items-center justify-between">
			<h2 class="font-medium">Run history</h2>
			<button type="button" onclick={() => invalidateAll()} class="text-xs" style="color: var(--color-muted)">Refresh</button>
		</div>
		{#if data.runs.length === 0}
			<p class="mt-2 text-sm" style="color: var(--color-muted)">No runs yet.</p>
		{:else}
			<table class="mt-2 w-full border-collapse text-sm">
				<thead>
					<tr class="text-left" style="color: var(--color-muted)">
						<th class="py-1 font-medium">Status</th>
						<th class="py-1 font-medium">Found</th>
						<th class="py-1 font-medium">Added</th>
						<th class="py-1 font-medium">Started</th>
						<th class="py-1 font-medium">Finished</th>
					</tr>
				</thead>
				<tbody>
					{#each data.runs as run (run.id)}
						<tr class="border-t" style="border-color: var(--color-border)">
							<td class="py-1" title={run.error ?? ''}>{run.status}</td>
							<td class="py-1">{run.itemsFound}</td>
							<td class="py-1">{run.itemsAdded}</td>
							<td class="py-1" style="color: var(--color-muted)">{fmt(run.startedAt)}</td>
							<td class="py-1" style="color: var(--color-muted)">{fmt(run.finishedAt)}</td>
						</tr>
					{/each}
				</tbody>
			</table>
		{/if}
	</div>
</section>
