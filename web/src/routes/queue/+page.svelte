<svelte:head><title>{pageTitle('Up next')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import SiteMark from '$lib/components/SiteMark.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import type { Paged, SearchHit } from '$lib/types';
	import LoadMore from '$lib/components/ui/LoadMore.svelte';
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
	import { BookOpen, Check, Clock, Link2, Star, Trash2, Undo2, Upload } from '@lucide/svelte';
	import { PRESETS, PRESET_LABELS, backWhen, resolvePreset, type SnoozePreset } from '$lib/snooze';
	import { confirmDialog } from '$lib/dialog.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state<string | null>(null);

	/**
	 * What has been fetched past the first twenty-five.
	 *
	 * The header counted everything waiting while the list stopped at twenty-five. Kept across
	 * the reloads that follow marking something done — working down a long queue should not
	 * throw you back to the top each time — and pruned by hand of whatever leaves it.
	 */
	let tail = $state<{ items: SearchHit[]; cursor: string | null } | null>(null);
	let loadingMore = $state(false);

	const queued = $derived.by(() => {
		const first = data.queue.items;
		const ids = new Set(first.map((h) => h.itemId));
		return [...first, ...(tail?.items ?? []).filter((h) => !ids.has(h.itemId))];
	});
	const nextCursor = $derived(tail ? tail.cursor : data.queue.nextCursor);

	function forget(itemId: string) {
		if (tail) tail = { ...tail, items: tail.items.filter((h) => h.itemId !== itemId) };
	}

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			const res = await api.get(`/search?status=unwatched&sort=queue&limit=25&cursor=${encodeURIComponent(nextCursor)}`);
			if (!res.ok) {
				toast.error(failureMessage(res, 'Could not load more.'));
				return;
			}
			const page = (await res.json()) as Paged<SearchHit>;
			tail = { items: [...(tail?.items ?? []), ...page.items], cursor: page.nextCursor };
		} finally {
			loadingMore = false;
		}
	}

	async function markWatched(itemId: string) {
		busy = itemId;
		const res = await api.patch(`/items/${itemId}`, { status: 'Watched' });
		busy = null;
		if (res.ok) {
			forget(itemId);
			await invalidateAll();
			toast.success('Marked done.', {
				action: {
					label: 'Undo',
					run: async () => {
						const again = await api.patch(`/items/${itemId}`, { status: 'Added' });
						if (again.ok) await invalidateAll();
						else toast.error(failureMessage(again, 'Could not undo that.'));
					}
				}
			});
		} else {
			toast.error(failureMessage(res, 'Could not mark that done.'));
		}
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
		if (res.ok) {
			forget(hit.itemId);
			await invalidateAll();
		} else {
			toast.error(failureMessage(res, 'Could not put that aside.'));
		}
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
		else toast.error(failureMessage(res, 'Could not bring that back.'));
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
		if (res.ok) {
			forget(hit.itemId);
			await invalidateAll();
		} else {
			toast.error(failureMessage(res, 'Could not move that to the trash.'));
		}
	}

	/**
	 * The two halves of a queue.
	 *
	 * Something started is the cheapest thing to finish, so it leads — and until progress was
	 * recorded, a twenty-two-minute piece read half of on the train looked exactly like one
	 * never opened, and kept being offered from the top.
	 */
	const started = $derived(queued.filter((h) => (h.readProgress ?? 0) > 0.02));
	const fresh = $derived(queued.filter((h) => (h.readProgress ?? 0) <= 0.02));

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

	{#if queued.length === 0}
		<div class="mt-8 rounded-card border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			{#if data.hasSaved}
				<p class="font-medium">Nothing waiting.</p>
				<p class="mt-1 text-sm" style="color: var(--color-muted)">
					Everything you have saved is marked done.
				</p>
			{:else}
				<!-- An account with nothing in it is not caught up, it has not started. -->
				<p class="font-medium">Nothing here yet.</p>
				<p class="mt-1 text-sm" style="color: var(--color-muted)">
					Anything you save turns up here until you mark it done.
				</p>
				<div class="mt-4 flex flex-wrap justify-center gap-2">
					<Button href="/save" variant="primary" icon={Link2}>Save a link</Button>
					<Button href="/import" icon={Upload}>Import a file</Button>
				</div>
			{/if}
		</div>
	{:else}
		<p class="mt-5 text-sm" style="color: var(--color-muted)">
			{data.queue.total} waiting
		</p>

		{#if started.length > 0}
			<h2 class="mt-5 t-subsection">Carry on</h2>
			<p class="text-xs" style="color: var(--color-muted)">
				Part way through. The quickest things to finish.
			</p>
			<ul class="mt-2 flex flex-col divide-y rounded-card border" style="border-color: var(--color-border)">
				{#each started as hit (hit.itemId)}
					{@render row(hit)}
				{/each}
			</ul>

			<h2 class="mt-6 t-subsection">Not started</h2>
		{/if}

		<ul class="mt-2 flex flex-col divide-y rounded-card border" style="border-color: var(--color-border)">
			{#each fresh as hit (hit.itemId)}
				{@render row(hit)}
			{/each}
		</ul>
		{#if nextCursor}
			<LoadMore
				onclick={loadMore}
				loading={loadingMore}
				remaining={data.queue.total != null ? data.queue.total - queued.length : null}
			/>
		{/if}
	{/if}

	{#if data.aside.items.length > 0}
		<!-- Visible, and reversible. Somewhere to look is the difference between putting
		     something aside and losing it. -->
		<details class="mt-6">
			<summary class="cursor-pointer text-sm font-medium">
				Put aside ({data.aside.total ?? data.aside.items.length})
			</summary>
			<ul class="mt-2 flex flex-col divide-y rounded-card border" style="border-color: var(--color-border)">
				{#each data.aside.items as hit (hit.itemId)}
					<li
						class="flex flex-col gap-2 p-3 sm:flex-row sm:items-center sm:gap-3"
						style="border-color: var(--color-border)"
					>
						<div class="min-w-0 flex-1">
							<a
								href={hit.link.url}
								target="_blank"
								rel="noopener noreferrer"
								class="break-words text-sm hover:underline"
							>{hit.link.title ?? hit.link.url}<span class="sr-only"> (opens in a new tab)</span></a>
							<p class="mt-0.5 text-xs" style="color: var(--color-muted)">
								{hit.snoozedUntil ? backWhen(hit.snoozedUntil) : ''}
								{#if (hit.snoozeCount ?? 0) > 1}
									· put aside {hit.snoozeCount} times
								{/if}
							</p>
						</div>
						<Button
							size="sm"
							icon={Undo2}
							onclick={() => wake(hit.itemId)}
							disabled={busy !== null}
							class="self-end sm:self-auto"
						>
							Bring it back
						</Button>
					</li>
				{/each}
			</ul>
		</details>
	{/if}
</Page>

{#snippet row(hit: SearchHit)}
	<!-- Stacked below `sm`: the actions were a fixed 210px cluster beside the title, which left
	     the title so little room that an address wrapped one character at a time. -->
	<li
		class="flex flex-col gap-2 p-3 transition-colors hover:bg-chip/60 sm:flex-row sm:items-start sm:gap-3"
		style="border-color: var(--color-border)"
	>
		<div class="flex min-w-0 flex-1 items-start gap-3">
					<SiteMark src={hit.link.favicon} class="mt-0.5" />

					<div class="min-w-0 flex-1">
						<a
							href={hit.link.url}
							target="_blank"
							rel="noopener noreferrer"
							class="break-words font-medium hover:underline"
						>{hit.link.title ?? hit.link.url}<span class="sr-only"> (opens in a new tab)</span></a>
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
		</div>

	<div class="flex shrink-0 flex-wrap items-center gap-1 self-end sm:self-auto">
		<!-- Five choices, not a date picker: "not now" is a feeling, and being made to pick a
		     Tuesday to express it is why snooze buttons go unused. -->
		<Menu triggerClass={buttonClass('secondary', 'sm')} title="Put this aside until later" align="end">
			{#snippet trigger()}
				<Clock size={15} aria-hidden="true" /> Snooze
			{/snippet}
			{#each PRESETS as preset (preset)}
				<MenuItem onselect={() => snooze(hit, preset)}>{PRESET_LABELS[preset]}</MenuItem>
			{/each}
		</Menu>

		{#if (hit.snoozeCount ?? 0) >= 3}
			<Button
				variant="ghost-danger"
				size="sm"
				icon={Trash2}
				iconOnly
				label={`Move to trash — put aside ${hit.snoozeCount} times`}
				onclick={() => letGo(hit)}
				disabled={busy !== null}
			/>
		{/if}

		<Button
			size="sm"
			icon={Check}
			onclick={() => markWatched(hit.itemId)}
			disabled={busy !== null}
			title="Mark done"
		>
			Done
		</Button>
	</div>
	</li>
{/snippet}
