<svelte:head><title>{pageTitle('Trash')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import { plural } from '$lib/labels';
	import { failureMessage } from '$lib/api/errors';
	import Button from '$lib/components/ui/Button.svelte';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import { toast } from '$lib/toast.svelte';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { ListMusic, Link2, RotateCcw, Trash2 } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state<string | null>(null);

	const total = $derived(data.trash.playlists.length + data.trash.items.length);

	/** "2 playlists and 1 link" — the two kinds are not interchangeable, so both are named. */
	const contents = $derived(
		[
			data.trash.playlists.length > 0 ? plural(data.trash.playlists.length, 'playlist') : null,
			data.trash.items.length > 0 ? plural(data.trash.items.length, 'link') : null
		]
			.filter(Boolean)
			.join(' and ')
	);

	/** "in 12 days", or "today" on the last day — how long is left to change your mind. */
	function purgesIn(purgeAfter: string): string {
		const days = Math.ceil((new Date(purgeAfter).getTime() - Date.now()) / 86_400_000);
		if (days <= 0) return 'purges today';
		return days === 1 ? 'purges tomorrow' : `purges in ${days} days`;
	}

	/** Removes one thing for good, when the whole trash is not what should go. */
	async function purge(kind: 'playlists' | 'items', id: string, name: string) {
		const ok = await confirmDialog(`Delete "${name}" for good?`, {
			description: 'It will not be recoverable from the trash any more.',
			danger: true,
			confirmLabel: 'Delete for good'
		});
		if (!ok) return;

		busy = id;
		const res = await api.del(`/trash/${kind}/${id}`);
		busy = null;
		if (res.ok || res.status === 204) {
			toast.success(`"${name}" deleted for good.`);
			await invalidateAll();
		} else {
			toast.error(failureMessage(res, 'Could not delete that.'));
		}
	}

	/** Puts back everything, playlists first so their links have somewhere to go. */
	async function restoreAll() {
		busy = 'all';
		let failed = 0;
		for (const playlist of data.trash.playlists) {
			const res = await api.post(`/trash/playlists/${playlist.id}/restore`).catch(() => null);
			if (!res?.ok) failed++;
		}
		for (const item of data.trash.items) {
			const res = await api.post(`/trash/items/${item.id}/restore`).catch(() => null);
			// Already back in its playlist counts as restored.
			if (!res?.ok && res?.status !== 409) failed++;
		}
		busy = null;
		await invalidateAll();
		if (failed === 0) toast.success('Everything is back.');
		else toast.error(`Could not restore ${failed} of them. They are still in the trash.`);
	}

	async function restore(kind: 'playlists' | 'items', id: string) {
		busy = id;
		const res = await api.post(`/trash/${kind}/${id}/restore`);
		busy = null;

		if (res.ok) {
			await invalidateAll();
		} else if (res.status === 409) {
			toast.error('That link is already back in the playlist.');
		} else {
			toast.error('Could not restore that. Try again.');
		}
	}

	async function empty() {
		const ok = await confirmDialog(
			`Permanently delete everything in the trash? ${contents} will be gone for good.`,
			{ danger: true, confirmLabel: 'Delete for good' }
		);
		if (!ok) return;

		busy = 'all';
		const res = await api.del('/trash');
		busy = null;

		if (res.ok) await invalidateAll();
		else toast.error('Could not empty the trash. Try again.');
	}
</script>

<Page width="medium">
	<PageHeader
		title="Trash"
		description={`Deleted playlists and links stay here for ${data.trash.retentionDays} days, then go for good.`}
	>
		{#snippet actions()}
			{#if total > 0}
				<Button icon={RotateCcw} onclick={restoreAll} loading={busy === 'all'} disabled={busy !== null}>
					Restore all
				</Button>
				<Button variant="danger-outline" icon={Trash2} onclick={empty} disabled={busy !== null}>
					Empty trash
				</Button>
			{/if}
		{/snippet}
	</PageHeader>


	{#if total === 0}
		<div class="mt-8 rounded-card border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing deleted.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Playlists and links you delete show up here, so you can put them back.
			</p>
		</div>
	{/if}

	{#if data.trash.playlists.length}
		<h2 class="mt-8 t-subsection" style="color: var(--color-muted)">Playlists</h2>
		<ul class="mt-2 flex flex-col divide-y rounded-card border" style="border-color: var(--color-border)">
			{#each data.trash.playlists as playlist (playlist.id)}
				<li class="flex items-center gap-3 p-3" style="border-color: var(--color-border)">
					<ListMusic size={18} aria-hidden="true" style="color: var(--color-muted)" class="shrink-0" />
					<div class="min-w-0 flex-1">
						<p class="truncate font-medium">{playlist.name}</p>
						<p class="text-xs" style="color: var(--color-muted)">
							{playlist.itemCount} {playlist.itemCount === 1 ? 'link' : 'links'} · {purgesIn(playlist.purgeAfter)}
						</p>
					</div>
					<Button size="sm" icon={RotateCcw} onclick={() => restore('playlists', playlist.id)} disabled={busy !== null}>
						Restore
					</Button>
					<Button
						variant="ghost-danger"
						size="sm"
						icon={Trash2}
						iconOnly
						label={`Delete ${playlist.name} for good`}
						onclick={() => purge('playlists', playlist.id, playlist.name)}
						disabled={busy !== null}
					/>
				</li>
			{/each}
		</ul>
	{/if}

	{#if data.trash.items.length}
		<h2 class="mt-8 t-subsection" style="color: var(--color-muted)">Links</h2>
		<ul class="mt-2 flex flex-col divide-y rounded-card border" style="border-color: var(--color-border)">
			{#each data.trash.items as item (item.id)}
				<li class="flex items-center gap-3 p-3" style="border-color: var(--color-border)">
					<Link2 size={18} aria-hidden="true" style="color: var(--color-muted)" class="shrink-0" />
					<div class="min-w-0 flex-1">
						<a
							href={item.url}
							target="_blank"
							rel="noopener noreferrer"
							class="block truncate hover:underline"
							title={item.url}
						>{item.title ?? item.url}</a>
						<p class="truncate text-xs" style="color: var(--color-muted)">
							from <a href={`/playlists/${item.playlistId}`} class="hover:underline">{item.playlistName}</a>
							· {purgesIn(item.purgeAfter)}
						</p>
					</div>
					<Button size="sm" icon={RotateCcw} onclick={() => restore('items', item.id)} disabled={busy !== null}>
						Restore
					</Button>
					<Button
						variant="ghost-danger"
						size="sm"
						icon={Trash2}
						iconOnly
						label={`Delete ${item.title ?? item.url} for good`}
						onclick={() => purge('items', item.id, item.title ?? item.url)}
						disabled={busy !== null}
					/>
				</li>
			{/each}
		</ul>
	{/if}
</Page>
