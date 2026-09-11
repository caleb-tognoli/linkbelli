<svelte:head><title>Profile - linkbelli</title></svelte:head>

<script lang="ts">
	import { api } from '$lib/api/client';
	import ApiKeysManager from '$lib/components/ApiKeysManager.svelte';
	import ThemeToggle from '$lib/components/ThemeToggle.svelte';
	import Switch from '$lib/components/Switch.svelte';
	import { Download } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	// Named by what the file is for, not by its extension — "OPML" means nothing until you know
	// it is the thing your feed reader imports.
	const EXPORTS = [
		{ format: 'json', label: 'Everything (JSON)', hint: 'Playlists, links, folders and sources' },
		{ format: 'csv', label: 'Links (CSV)', hint: 'One row per link — reads back into the importer' },
		{ format: 'html', label: 'Bookmarks (HTML)', hint: 'Import into any browser' },
		{ format: 'opml', label: 'Feeds (OPML)', hint: 'Your RSS sources, for a feed reader' }
	];

	let showNsfw = $state(data.user?.showNsfw ?? false);

	async function setNsfw(value: boolean) {
		showNsfw = value;
		await api.put('/me/preferences', { showNsfw: value });
	}

	function pct(used: number, max: number) {
		return max > 0 ? Math.min(100, Math.round((used / max) * 100)) : 0;
	}
</script>

<section class="mx-auto flex max-w-4xl flex-col gap-10">
	<h1 class="text-2xl font-semibold">Profile</h1>

	<div>
		<h2 class="font-medium">Theme</h2>
		<div class="mt-3">
			<ThemeToggle initial={data.theme} />
		</div>
	</div>

	<div>
		<h2 class="font-medium">Content</h2>
		<label class="mt-3 flex items-center gap-2 text-sm">
			<Switch checked={showNsfw} onchange={setNsfw} />
			Show NSFW
		</label>
	</div>

	<div>
		<h2 class="font-medium">Profile</h2>
		{#if data.user}
			<dl
				class="mt-3 grid grid-cols-[8rem_1fr] gap-y-2 rounded-lg border p-4 text-sm"
				style="border-color: var(--color-border); background: var(--color-surface)"
			>
				<dt style="color: var(--color-muted)">Username</dt>
				<dd>{data.user.username ?? '—'}</dd>
				<dt style="color: var(--color-muted)">Email</dt>
				<dd>{data.user.email ?? '—'}</dd>
			</dl>
		{/if}
	</div>

	{#if data.quota}
		<div>
			<h2 class="font-medium">Quota</h2>
			<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
				{#each [
					{ label: 'Sources', used: data.quota.sourcesUsed, max: data.quota.maxSources },
					{ label: 'Runs today', used: data.quota.runsUsedToday, max: data.quota.maxRunsPerDay },
					{ label: 'Items / run', used: 0, max: data.quota.maxItemsPerRun }
				] as q (q.label)}
					<div
						class="rounded-lg border p-3"
						style="border-color: var(--color-border); background: var(--color-surface)"
					>
						<div class="text-sm" style="color: var(--color-muted)">{q.label}</div>
						<div class="mt-1 text-lg font-semibold">
							{#if q.label === 'Items / run'}{q.max}{:else}{q.used} / {q.max}{/if}
						</div>
						{#if q.label !== 'Items / run'}
							<div class="mt-2 h-1.5 overflow-hidden rounded-full" style="background: var(--color-border)">
								<div
									class="h-full"
									style="width: {pct(q.used, q.max)}%; background: var(--color-accent)"
								></div>
							</div>
						{/if}
					</div>
				{/each}
			</div>
		</div>
	{/if}

	<div>
		<h2 class="font-medium">Export</h2>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Download everything you have here. It is your data; take it wherever you like.
		</p>
		<div class="mt-3 flex flex-wrap gap-2">
			{#each EXPORTS as fmt (fmt.format)}
				<a
					href={`/api/v1/export?format=${fmt.format}`}
					download
					class="inline-flex items-center gap-1.5 rounded-md border px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
					title={fmt.hint}
				>
					<Download size={15} aria-hidden="true" />
					{fmt.label}
				</a>
			{/each}
		</div>
	</div>

	<div>
		<ApiKeysManager keys={data.apiKeys} />
	</div>
</section>
