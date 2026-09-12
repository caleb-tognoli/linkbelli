<script lang="ts">
	import { api, json } from '$lib/api/client';
	import Switch from '$lib/components/Switch.svelte';

	interface Prefs {
		onShare: boolean;
		onFollow: boolean;
		onSourceStopped: boolean;
		weeklyDigest: boolean;
	}

	/**
	 * Described by what arrives, not by a setting name.
	 *
	 * "Share notifications" tells somebody nothing about how often their inbox will ring, which is
	 * the only thing they actually want to know before deciding.
	 */
	const ROWS = [
		{
			key: 'onShare' as const,
			label: 'Somebody shares a playlist with me',
			hint: 'Rare, and you would not find out any other way.'
		},
		{
			key: 'onSourceStopped' as const,
			label: 'One of my sources stops working',
			hint: 'Sent once, when a source gives up after repeated failures.'
		},
		{
			key: 'onFollow' as const,
			label: 'Somebody follows one of my playlists',
			hint: 'Can happen often on a public playlist.'
		},
		{
			key: 'weeklyDigest' as const,
			label: 'A weekly summary',
			hint: 'What arrived, what is still unread, and any source that went quiet.'
		}
	];

	let prefs = $state<Prefs | null>(null);
	let error = $state<string | null>(null);

	$effect(() => {
		void load();
	});

	async function load() {
		try {
			prefs = await json<Prefs>(await api.get('/notifications'));
			error = null;
		} catch {
			error = 'Could not load these.';
		}
	}

	async function set(key: keyof Prefs, value: boolean) {
		if (!prefs) return;

		const previous = prefs[key];
		prefs = { ...prefs, [key]: value };

		// Only the one that changed. Every field is optional on the API, so this cannot reach
		// across and undo another switch.
		const res = await api.put('/notifications', { [key]: value });
		if (!res.ok) {
			prefs = { ...prefs, [key]: previous };
			error = 'Could not save that.';
		}
	}
</script>

<div>
	<h2 class="font-medium">Email</h2>
	<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
		Nothing here is marketing, and every message carries a link that turns that kind off. The
		two about your own things are on; the two that repeat are not, unless you say so.
	</p>

	{#if error}
		<p class="mt-3 text-sm" style="color: var(--color-danger)">{error}</p>
	{/if}

	{#if prefs}
		<ul class="mt-3 flex flex-col gap-3">
			{#each ROWS as row (row.key)}
				<li class="flex items-start gap-3">
					<div class="mt-0.5 shrink-0">
						<Switch checked={prefs[row.key]} onchange={(v) => set(row.key, v)} />
					</div>
					<div class="min-w-0">
						<p class="text-sm">{row.label}</p>
						<p class="text-xs" style="color: var(--color-muted)">{row.hint}</p>
					</div>
				</li>
			{/each}
		</ul>
	{:else if !error}
		<p class="mt-3 text-sm" style="color: var(--color-muted)">Looking…</p>
	{/if}
</div>
