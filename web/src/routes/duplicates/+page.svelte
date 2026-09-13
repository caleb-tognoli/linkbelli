<svelte:head><title>Duplicates - linkbelli</title></svelte:head>

<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { CopyCheck, Trash2 } from '@lucide/svelte';
	import type { DuplicateCopy, DuplicateGroup } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let busy = $state<string | null>(null);
	let error = $state<string | null>(null);

	const total = $derived(data.groups.reduce((sum, g) => sum + g.copies.length - 1, 0));

	function describe(group: DuplicateGroup): string {
		if (group.kind === 'SameLink') return 'The same link, saved more than once';
		if (group.kind === 'SamePage') return 'The same page, reached by different addresses';
		return 'These addresses all lead to the same page';
	}

	/**
	 * Whether this group is a guess rather than a fact.
	 *
	 * The first two kinds are certainties — the identical link, or the identical host and path.
	 * A shared redirect target is not: a consent page, a paywall and a "this has moved" stub all
	 * land somewhere shared without being the same page. Worth surfacing, worth labelling.
	 */
	const isSuggestion = (group: DuplicateGroup) => group.kind === 'SameAfterRedirect';

	/** Removes every copy but the one the person chose to keep. */
	async function keepOnly(group: DuplicateGroup, keep: DuplicateCopy) {
		const drop = group.copies.filter((c) => c.itemId !== keep.itemId);
		const ok = await confirmDialog(
			`Remove ${drop.length} other ${drop.length === 1 ? 'copy' : 'copies'}, keeping the one in "${keep.playlistName}"? You can put them back from the trash.`,
			{ danger: true, confirmLabel: 'Remove the rest' }
		);
		if (!ok) return;

		busy = group.key;
		error = null;
		const res = await api.post('/items/bulk', {
			itemIds: drop.map((c) => c.itemId),
			action: 'Delete'
		});
		busy = null;

		if (res.ok) await invalidateAll();
		else error = 'Could not remove those. Try again.';
	}
</script>

<section class="mx-auto max-w-4xl">
	<header>
		<h1 class="text-2xl font-semibold">Duplicates</h1>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			The same thing saved more than once — the identical link in several playlists, one page
			reached by different addresses, or several addresses that all redirect to one page.
		</p>
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

	{#if data.groups.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">Nothing saved twice.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Links are deduplicated as they arrive, so this stays empty until the same page turns up
				under two different addresses.
			</p>
		</div>
	{:else}
		<p class="mt-5 text-sm" style="color: var(--color-muted)">
			{data.groups.length} {data.groups.length === 1 ? 'group' : 'groups'} · {total} extra
			{total === 1 ? 'copy' : 'copies'}
		</p>

		<ul class="mt-3 flex flex-col gap-3">
			{#each data.groups as group (group.key)}
				<li class="rounded-lg border p-4" style="border-color: var(--color-border)">
					<div class="flex items-start gap-2">
						<CopyCheck size={16} aria-hidden="true" class="mt-0.5 shrink-0" style="color: var(--color-muted)" />
						<div class="min-w-0">
							<p class="text-sm font-medium">
								{describe(group)}
								{#if isSuggestion(group)}
									<span
										class="ml-1.5 rounded px-1.5 py-0.5 align-middle text-xs font-normal"
										style="background: var(--color-border); color: var(--color-muted)"
									>worth checking</span>
								{/if}
							</p>
							<p class="truncate text-xs" style="color: var(--color-muted)">{group.key}</p>
							{#if isSuggestion(group)}
								<p class="mt-1 text-xs" style="color: var(--color-muted)">
									Following each address ended up here. Usually that means one page — but a
									consent screen or a "this has moved" stub lands somewhere shared too, so have a
									look before removing anything.
								</p>
							{/if}
						</div>
					</div>

					<ul class="mt-3 flex flex-col divide-y" style="border-color: var(--color-border)">
						{#each group.copies as copy (copy.itemId)}
							<li class="flex items-center gap-3 py-2" style="border-color: var(--color-border)">
								<div class="min-w-0 flex-1">
									<p class="truncate text-sm">{copy.title ?? copy.url}</p>
									<p class="truncate text-xs" style="color: var(--color-muted)">
										in <a href={`/playlists/${copy.playlistId}`} class="hover:underline">{copy.playlistName}</a>
									</p>
								</div>
								<button
									type="button"
									onclick={() => keepOnly(group, copy)}
									disabled={busy !== null}
									class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
									style="border-color: var(--color-border)"
									title="Keep this one and remove the others"
								>
									<Trash2 size={14} aria-hidden="true" /> Keep this
								</button>
							</li>
						{/each}
					</ul>
				</li>
			{/each}
		</ul>
	{/if}
</section>
