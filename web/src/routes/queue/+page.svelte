<svelte:head><title>Up next - linkbelli</title></svelte:head>

<script lang="ts">
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import { buttonClass } from '$lib/components/ui/Button.svelte';
	import MenuItem from '$lib/components/ui/MenuItem.svelte';
	import Menu from '$lib/components/ui/Menu.svelte';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import NsfwBadge from '$lib/components/NsfwBadge.svelte';
	import { BookOpen, Check, Clock, Star, Trash2 } from '@lucide/svelte';
	import { PRESETS, PRESET_LABELS, backWhen, resolvePreset, type SnoozePreset } from '$lib/snooze';
	import { confirmDialog } from '$lib/dialog.svelte';
	import type { SearchHit } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state<string | null>(null);


	async function markWatched(itemId: string) {
		busy = itemId;
		const res = await api.patch(`/items/${itemId}`, { status: 'Watched' });
		busy = null;
		if (res.ok) await invalidateAll();
		else toast.error(failureMessage(res.status, 'Could not mark that done.'));
	}

	/**
	 * Puts something aside until a moment worked out here.
	 *
	 * Resolved in the browser rather than by name, because the browser is the only thing that
	 * knows what evening means where the reader is.
	 */
	async function snooze(hit: SearchHit, preset: SnoozePreset) {
		busy = hit.itemId;
		const res = await api.post(`/items/${hit.itemId}/snooze`, {
			until: resolvePreset(preset).toISOString()
		});
		busy = null;
		if (res.ok) await invalidateAll();
		else toast.error(failureMessage(res.status, 'Could not put that aside.'));
	}

	/**
	 * Offered once something has been put aside three times.
	 *
	 * An item passed over that often is a signal. Saying so is kinder than re-offering it
	 * forever, and kinder than letting somebody feel guilty about a list they will never read.
	 */
	async function wake(itemId: string) {
		busy = itemId;
		const res = await api.del(`/items/${itemId}/snooze`);
		busy = null;
		if (res.ok) await invalidateAll();
		else toast.error(failureMessage(res.status, 'Could not bring that back.'));
	}

	async function letGo(hit: SearchHit) {
		const ok = await confirmDialog(
			`You have put "${hit.link.title ?? hit.link.url}" aside ${hit.snoozeCount} times. Move it to the trash? You can get it back from there.`,
			{ danger: true, confirmLabel: 'Move to trash' }
		);
		if (!ok) return;

		busy = hit.itemId;
		const res = await api.del(`/items/${hit.itemId}`);
		busy = null;
		if (res.ok) await invalidateAll();
		else toast.error(failureMessage(res.status, 'Could not move that to the trash.'));
	}

	/**
	 * The two halves of a queue.
	 *
	 * Something started is the cheapest thing to finish, so it leads — and until progress was
	 * recorded, a twenty-two-minute piece read half of on the train looked exactly like one
	 * never opened, and kept being offered from the top.
	 */
	const started = $derived(data.queue.items.filter((h) => (h.readProgress ?? 0) > 0.02));
	const fresh = $derived(data.queue.items.filter((h) => (h.readProgress ?? 0) <= 0.02));

	/** How long something has been waiting — the reason it is near the top. */
	function waiting(iso: string): string {
		const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
		if (days < 1) return 'today';
		if (days === 1) return 'since yesterday';
		if (days < 30) return `for ${days} days`;
		const months = Math.floor(days / 30);
		return months < 12 ? `for ${months} months` : `for over a year`;
	}
</script>

<Page width="narrow">
	<PageHeader
		title="Up next"
		description="Everything you have not got to yet, across every playlist — what you started first, then what you rated highly, then whatever you have been carrying longest."
	/>

	{#if data.queue.items.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing waiting.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Everything you have saved is marked watched.
			</p>
		</div>
	{:else}
		<p class="mt-5 text-sm" style="color: var(--color-muted)">
			{data.queue.total} waiting
		</p>

		{#if started.length > 0}
			<h2 class="mt-5 text-sm font-medium">Carry on</h2>
			<p class="text-xs" style="color: var(--color-muted)">
				Part way through. The quickest things to finish.
			</p>
			<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
				{#each started as hit (hit.itemId)}
					{@render row(hit)}
				{/each}
			</ul>

			<h2 class="mt-6 text-sm font-medium">Not started</h2>
		{/if}

		<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each fresh as hit (hit.itemId)}
				{@render row(hit)}
			{/each}
		</ul>
	{/if}

	{#if data.aside.items.length > 0}
		<!-- Visible, and reversible. Somewhere to look is the difference between putting
		     something aside and losing it. -->
		<details class="mt-6">
			<summary class="cursor-pointer text-sm font-medium">
				Put aside ({data.aside.total ?? data.aside.items.length})
			</summary>
			<ul class="mt-2 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
				{#each data.aside.items as hit (hit.itemId)}
					<li class="flex items-center gap-3 p-3" style="border-color: var(--color-border)">
						<div class="min-w-0 flex-1">
							<a
								href={hit.link.url}
								target="_blank"
								rel="noopener noreferrer"
								class="break-words text-sm hover:underline"
							>{hit.link.title ?? hit.link.url}</a>
							<p class="mt-0.5 text-xs" style="color: var(--color-muted)">
								{hit.snoozedUntil ? backWhen(hit.snoozedUntil) : ''}
								{#if (hit.snoozeCount ?? 0) > 1}
									· put aside {hit.snoozeCount} times
								{/if}
							</p>
						</div>
						<button
							type="button"
							onclick={() => wake(hit.itemId)}
							disabled={busy !== null}
							class="inline-flex shrink-0 items-center rounded-md border px-2.5 py-1.5 text-sm disabled:opacity-60"
							style="border-color: var(--color-border)"
						>Bring it back</button>
					</li>
				{/each}
			</ul>
		</details>
	{/if}
</Page>

{#snippet row(hit: SearchHit)}
	<li class="flex items-start gap-3 p-3" style="border-color: var(--color-border)">
					{#if hit.link.favicon}
						<img src={hit.link.favicon} alt="" class="mt-0.5 size-4 shrink-0 object-contain" loading="lazy" />
					{:else}
						<span class="mt-0.5 size-4 shrink-0 rounded-sm" style="background: var(--color-border)"></span>
					{/if}

					<div class="min-w-0 flex-1">
						<a
							href={hit.link.url}
							target="_blank"
							rel="noopener noreferrer"
							class="break-words font-medium hover:underline"
						>{hit.link.title ?? hit.link.url}</a>
						{#if hit.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}

						{#if hit.note}
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">{hit.note}</p>
						{/if}

						<p class="mt-1 flex flex-wrap items-center gap-x-2 text-xs" style="color: var(--color-muted)">
							<a href={`/playlists/${hit.playlistId}`} class="hover:underline">{hit.playlistName}</a>
							<span aria-hidden="true">·</span>
							<span>waiting {waiting(hit.addedAt)}</span>
							{#if (hit.readProgress ?? 0) > 0.02}
								<span aria-hidden="true">·</span>
								<a
									href={`/read/${hit.link.id}?from=${hit.playlistId}`}
									class="inline-flex items-center gap-1 hover:underline"
								>
									<BookOpen size={12} aria-hidden="true" />
									{Math.round((hit.readProgress ?? 0) * 100)}% read
								</a>
							{:else if hit.link.wordCount}
								<span aria-hidden="true">·</span>
								<a
									href={`/read/${hit.link.id}?from=${hit.playlistId}`}
									class="inline-flex items-center gap-1 hover:underline"
								>
									<BookOpen size={12} aria-hidden="true" /> Read it here
								</a>
							{/if}
							{#if hit.score !== null}
								<span aria-hidden="true">·</span>
								<span class="inline-flex items-center gap-1 tabular-nums">
									<Star size={12} aria-hidden="true" /> {hit.score}
								</span>
							{/if}
						</p>
					</div>

	<div class="flex shrink-0 items-center gap-1">
		<!-- Five choices, not a date picker: "not now" is a feeling, and being made to pick a
		     Tuesday to express it is why snooze buttons go unused. -->
		<Menu triggerClass={buttonClass('secondary', 'sm')} title="Not now" align="end">
			{#snippet trigger()}
				<Clock size={15} aria-hidden="true" /> Not now
			{/snippet}
			{#each PRESETS as preset (preset)}
				<MenuItem onselect={() => snooze(hit, preset)}>{PRESET_LABELS[preset]}</MenuItem>
			{/each}
		</Menu>

		{#if (hit.snoozeCount ?? 0) >= 3}
			<button
				type="button"
				onclick={() => letGo(hit)}
				disabled={busy !== null}
				class="inline-flex items-center rounded-md border p-1.5 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border); color: var(--color-danger)"
				title="Put aside {hit.snoozeCount} times — let it go?"
				aria-label="Move to trash"
			>
				<Trash2 size={14} aria-hidden="true" />
			</button>
		{/if}

		<button
			type="button"
			onclick={() => markWatched(hit.itemId)}
			disabled={busy !== null}
			class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
			style="border-color: var(--color-border)"
			title="Mark watched"
		>
			<Check size={14} aria-hidden="true" /> Done
		</button>
	</div>
	</li>
{/snippet}
