<svelte:head><title>Tags - linkbelli</title></svelte:head>

<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog, promptDialog } from '$lib/dialog.svelte';
	import { Merge, Pencil, Tags, Trash2 } from '@lucide/svelte';
	import type { TagChange, TagUsage } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let filter = $state('');
	let busy = $state<string | null>(null);
	let error = $state<string | null>(null);
	let done = $state<string | null>(null);

	const shown = $derived(
		filter.trim()
			? data.tags.filter((t) => t.name.includes(filter.trim().toLowerCase()))
			: data.tags
	);

	/**
	 * Mirrors TagNormalizer on the server: lowercase, collapse whitespace, cap at 50.
	 *
	 * Only used to work out whether a typed name lands on a tag that already exists, so the
	 * confirmation can say "merge" rather than "rename". The server normalizes again and is the
	 * authority; getting this wrong misphrases a prompt, it does not change what happens.
	 */
	function normalize(raw: string): string {
		return raw.trim().toLowerCase().split(/\s+/).join(' ').slice(0, 50);
	}

	/** A tag stripped to its letters and digits, singular — "front-end", "front end" and "frontend" all land here. */
	function squash(name: string): string {
		const bare = name.replace(/[^a-z0-9]/g, '');
		return bare.endsWith('s') && bare.length > 3 ? bare.slice(0, -1) : bare;
	}

	/**
	 * Tags that differ only by punctuation, spacing or a plural.
	 *
	 * Deliberately narrow. Normalization already folds case and whitespace, so what is left over
	 * is punctuation and plurals — and those are near-certain duplicates. A looser rule (edit
	 * distance, prefixes) would put "rust" beside "rusty" and suggest destroying one of them.
	 */
	const similar = $derived.by(() => {
		const groups = new Map<string, TagUsage[]>();
		for (const tag of data.tags) {
			const key = squash(tag.name);
			if (!key) continue;
			groups.set(key, [...(groups.get(key) ?? []), tag]);
		}
		return [...groups.values()].filter((g) => g.length > 1);
	});

	const uses = (tag: TagUsage) => tag.playlistCount + tag.itemCount;

	function count(n: number, one: string, many = one + 's') {
		return `${n} ${n === 1 ? one : many}`;
	}

	/** "3 playlists and 12 links", dropping whichever half is zero — never "and 0 links". */
	function both(playlists: number, items: number): string {
		const parts = [];
		if (playlists) parts.push(count(playlists, 'playlist'));
		if (items) parts.push(count(items, 'link'));
		return parts.join(' and ') || 'nothing';
	}

	const impact = (tag: TagUsage) => both(tag.playlistCount, tag.itemCount);
	const moved = (c: TagChange) => both(c.playlists, c.items);

	async function run(tag: string, body: () => Promise<Response>, describe: (c: TagChange) => string) {
		busy = tag;
		error = null;
		done = null;
		const res = await body();
		busy = null;

		if (!res.ok) {
			error =
				res.status === 404
					? `"${tag}" is not one of your tags any more. Reload the page.`
					: 'That did not work. Try again.';
			return;
		}

		done = describe((await res.json()) as TagChange);
		await invalidateAll();
	}

	async function rename(tag: TagUsage) {
		const typed = await promptDialog(
			`Rename "${tag.name}" to:`,
			tag.name,
			{ confirmLabel: 'Rename' }
		);
		if (typed === null) return;

		const target = normalize(typed);
		if (!target || target === tag.name) return;

		// Renaming onto a tag you already use is a merge, and a merge is destructive: the two
		// sets of links end up under one name with no way back. Say so, with the numbers, before
		// it runs rather than after.
		const existing = data.tags.find((t) => t.name === target);
		if (existing) {
			const ok = await confirmDialog(
				`"${target}" already exists. Merging will move ${impact(tag)} from "${tag.name}" onto it, ` +
					`and "${tag.name}" will be gone. This cannot be undone.`,
				{ danger: true, confirmLabel: 'Merge' }
			);
			if (!ok) return;
		}

		await run(
			tag.name,
			() => api.post('/tags/rename', { from: tag.name, to: target }),
			(c) =>
				c.merged
					? `Merged into "${target}" — ${moved(c)} moved.`
					: `Renamed to "${target}" — ${moved(c)} updated.`
		);
	}

	async function remove(tag: TagUsage) {
		const ok = await confirmDialog(
			`Remove "${tag.name}" from ${impact(tag)}? The links themselves stay where they are.`,
			{ danger: true, confirmLabel: 'Remove tag' }
		);
		if (!ok) return;

		await run(
			tag.name,
			() => api.post('/tags/delete', { name: tag.name }),
			(c) => `Removed "${tag.name}" from ${moved(c)}.`
		);
	}

	/** Merge the smaller of a similar pair into the larger — the direction that moves fewest rows. */
	async function mergeGroup(group: TagUsage[]) {
		const [winner, ...losers] = [...group].sort((a, b) => uses(b) - uses(a));
		const loser = losers[0];

		const ok = await confirmDialog(
			`Merge "${loser.name}" into "${winner.name}"? That moves ${impact(loser)}, and "${loser.name}" will be gone. ` +
				`This cannot be undone.`,
			{ danger: true, confirmLabel: 'Merge' }
		);
		if (!ok) return;

		await run(
			loser.name,
			() => api.post('/tags/rename', { from: loser.name, to: winner.name }),
			(c) => `Merged "${loser.name}" into "${winner.name}" — ${moved(c)} moved.`
		);
	}
</script>

<section class="mx-auto max-w-3xl">
	<header>
		<h1 class="flex items-center gap-2 text-2xl font-semibold">
			<Tags size={24} aria-hidden="true" /> Tags
		</h1>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Every tag across your playlists and links. Renaming one onto another merges them.
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

	{#if done}
		<p class="mt-4 rounded-md border px-3 py-2 text-sm" style="border-color: var(--color-border)" role="status">
			{done}
		</p>
	{/if}

	{#if data.tags.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">No tags yet.</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				Tag a playlist or a link and it will show up here.
			</p>
		</div>
	{:else}
		{#if similar.length > 0}
			<!-- The whole reason to look at this screen: two spellings of one subject. -->
			<div class="mt-6 rounded-lg border p-4" style="border-color: var(--color-border)">
				<h2 class="text-sm font-medium">These look like the same tag</h2>
				<ul class="mt-2 flex flex-col gap-2">
					{#each similar as group (group[0].name)}
						<li class="flex flex-wrap items-center justify-between gap-2 text-sm">
							<span>
								{#each group as tag, i (tag.name)}{i > 0 ? ' · ' : ''}<span class="font-medium"
										>{tag.name}</span
									><span style="color: var(--color-muted)"> ({uses(tag)})</span>{/each}
							</span>
							{#if group.length === 2}
								<button
									type="button"
									onclick={() => mergeGroup(group)}
									disabled={busy !== null}
									class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm disabled:opacity-60"
									style="border-color: var(--color-border)"
								>
									<Merge size={14} aria-hidden="true" /> Merge
								</button>
							{/if}
						</li>
					{/each}
				</ul>
			</div>
		{/if}

		<label class="mt-6 block">
			<span class="sr-only">Filter tags</span>
			<input
				type="search"
				bind:value={filter}
				placeholder="Filter tags"
				class="w-full rounded-md border px-3 py-2 text-sm"
				style="border-color: var(--color-border-strong); background: var(--color-bg)"
			/>
		</label>

		<ul class="mt-3 divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each shown as tag (tag.name)}
				<li class="flex flex-wrap items-center justify-between gap-2 px-3 py-2.5" style="border-color: var(--color-border)">
					<div class="min-w-0">
						<p class="truncate font-medium">{tag.name}</p>
						<p class="text-xs" style="color: var(--color-muted)">
							<!-- Both counts, always: a tag on fifty links and no playlists is not an unused tag. -->
							{count(tag.playlistCount, 'playlist')} · {count(tag.itemCount, 'link')}
						</p>
					</div>
					<div class="flex shrink-0 items-center gap-1">
						<button
							type="button"
							onclick={() => rename(tag)}
							disabled={busy !== null}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
							title="Rename or merge"
							aria-label="Rename or merge {tag.name}"
						>
							<Pencil size={16} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => remove(tag)}
							disabled={busy !== null}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
							style="color: var(--color-danger)"
							title="Remove everywhere"
							aria-label="Remove {tag.name} everywhere"
						>
							<Trash2 size={16} aria-hidden="true" />
						</button>
					</div>
				</li>
			{:else}
				<li class="px-3 py-6 text-center text-sm" style="color: var(--color-muted)">
					No tag matches "{filter}".
				</li>
			{/each}
		</ul>
	{/if}
</section>
