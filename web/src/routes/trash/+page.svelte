<svelte:head><title>Trash - linkbelli</title></svelte:head>

<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { ListMusic, Link2, RotateCcw, Trash2 } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state<string | null>(null);
	let error = $state<string | null>(null);

	const total = $derived(data.trash.playlists.length + data.trash.items.length);

	/** "in 12 days", or "today" on the last day — how long is left to change your mind. */
	function purgesIn(purgeAfter: string): string {
		const days = Math.ceil((new Date(purgeAfter).getTime() - Date.now()) / 86_400_000);
		if (days <= 0) return 'purges today';
		return days === 1 ? 'purges tomorrow' : `purges in ${days} days`;
	}

	async function restore(kind: 'playlists' | 'items', id: string) {
		busy = id;
		error = null;
		const res = await api.post(`/trash/${kind}/${id}/restore`);
		busy = null;

		if (res.ok) {
			await invalidateAll();
		} else if (res.status === 409) {
			error = 'That link is already back in the playlist.';
		} else {
			error = 'Could not restore that. Try again.';
		}
	}

	async function empty() {
		const ok = await confirmDialog(
			`Permanently delete everything in the trash? ${total} ${total === 1 ? 'thing' : 'things'} will be gone for good.`,
			{ danger: true, confirmLabel: 'Delete for good' }
		);
		if (!ok) return;

		busy = 'all';
		error = null;
		const res = await api.del('/trash');
		busy = null;

		if (res.ok) await invalidateAll();
		else error = 'Could not empty the trash. Try again.';
	}
</script>

<section class="mx-auto max-w-4xl">
	<header class="flex flex-wrap items-center justify-between gap-2">
		<div>
			<h1 class="text-2xl font-semibold">Trash</h1>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Deleted playlists and links stay here for {data.trash.retentionDays} days, then go for good.
			</p>
		</div>
		{#if total > 0}
			<button
				type="button"
				onclick={empty}
				disabled={busy !== null}
				class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-3 py-2 text-sm font-medium disabled:opacity-60"
				style="border-color: var(--color-border); color: var(--color-danger)"
			>
				<Trash2 size={16} aria-hidden="true" /> Empty trash
			</button>
		{/if}
	</header>

	{#if error}
		<p
			class="mt-4 rounded-md border px-3 py-2 text-sm"
			style="border-color: var(--color-danger); color: var(--color-danger)"
			role="alert"
		>
			{error}
		</p>
	{/if}

	{#if total === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing deleted.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Playlists and links you delete show up here, so you can put them back.
			</p>
		</div>
	{/if}

	{#if data.trash.playlists.length}
		<h2 class="mt-8 text-sm font-medium" style="color: var(--color-muted)">Playlists</h2>
		<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each data.trash.playlists as playlist (playlist.id)}
				<li class="flex items-center gap-3 p-3" style="border-color: var(--color-border)">
					<ListMusic size={18} aria-hidden="true" style="color: var(--color-muted)" class="shrink-0" />
					<div class="min-w-0 flex-1">
						<p class="truncate font-medium">{playlist.name}</p>
						<p class="text-xs" style="color: var(--color-muted)">
							{playlist.itemCount} {playlist.itemCount === 1 ? 'link' : 'links'} · {purgesIn(playlist.purgeAfter)}
						</p>
					</div>
					<button
						type="button"
						onclick={() => restore('playlists', playlist.id)}
						disabled={busy !== null}
						class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						<RotateCcw size={15} aria-hidden="true" /> Restore
					</button>
				</li>
			{/each}
		</ul>
	{/if}

	{#if data.trash.items.length}
		<h2 class="mt-8 text-sm font-medium" style="color: var(--color-muted)">Links</h2>
		<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each data.trash.items as item (item.id)}
				<li class="flex items-center gap-3 p-3" style="border-color: var(--color-border)">
					<Link2 size={18} aria-hidden="true" style="color: var(--color-muted)" class="shrink-0" />
					<div class="min-w-0 flex-1">
						<p class="truncate">{item.title ?? item.url}</p>
						<p class="truncate text-xs" style="color: var(--color-muted)">
							from <a href={`/playlists/${item.playlistId}`} class="hover:underline">{item.playlistName}</a>
							· {purgesIn(item.purgeAfter)}
						</p>
					</div>
					<button
						type="button"
						onclick={() => restore('items', item.id)}
						disabled={busy !== null}
						class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						<RotateCcw size={15} aria-hidden="true" /> Restore
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</section>
