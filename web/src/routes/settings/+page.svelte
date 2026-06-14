<script lang="ts">
	import ApiKeysManager from '$lib/components/ApiKeysManager.svelte';
	import ThemeToggle from '$lib/components/ThemeToggle.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	function pct(used: number, max: number) {
		return max > 0 ? Math.min(100, Math.round((used / max) * 100)) : 0;
	}
</script>

<section class="mx-auto flex max-w-3xl flex-col gap-10">
	<h1 class="text-2xl font-semibold">Settings</h1>

	<div>
		<h2 class="font-medium">Appearance</h2>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">Choose your theme.</p>
		<div class="mt-3">
			<ThemeToggle initial={data.theme} />
		</div>
	</div>

	<div>
		<h2 class="font-medium">Profile</h2>
		{#if data.user}
			<dl class="mt-3 grid grid-cols-[8rem_1fr] gap-y-2 rounded-lg border p-4 text-sm" style="border-color: var(--color-border); background: var(--color-surface)">
				<dt style="color: var(--color-muted)">User ID</dt>
				<dd class="font-mono">{data.user.userId}</dd>
				<dt style="color: var(--color-muted)">Auth method</dt>
				<dd>{data.user.authMethod}</dd>
			</dl>
		{/if}
	</div>

	{#if data.quota}
		<div>
			<h2 class="font-medium">Quota</h2>
			<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
				{#each [{ label: 'Sources', used: data.quota.sourcesUsed, max: data.quota.maxSources }, { label: 'Runs today', used: data.quota.runsUsedToday, max: data.quota.maxRunsPerDay }, { label: 'Items / run', used: 0, max: data.quota.maxItemsPerRun }] as q (q.label)}
					<div class="rounded-lg border p-3" style="border-color: var(--color-border); background: var(--color-surface)">
						<div class="text-sm" style="color: var(--color-muted)">{q.label}</div>
						<div class="mt-1 text-lg font-semibold">
							{#if q.label === 'Items / run'}{q.max}{:else}{q.used} / {q.max}{/if}
						</div>
						{#if q.label !== 'Items / run'}
							<div class="mt-2 h-1.5 overflow-hidden rounded-full" style="background: var(--color-border)">
								<div class="h-full" style={`width: ${pct(q.used, q.max)}%; background: var(--color-accent)`}></div>
							</div>
						{/if}
					</div>
				{/each}
			</div>
		</div>
	{/if}

	<div>
		<h2 class="font-medium">API keys</h2>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Programmatic access via the <code>X-Api-Key</code> header. The full key is shown once.
		</p>
		<div class="mt-3">
			<ApiKeysManager keys={data.apiKeys} />
		</div>
	</div>
</section>
