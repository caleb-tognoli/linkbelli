<svelte:head><title>Search - linkbelli</title></svelte:head>

<script lang="ts">
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { readingLabel } from '$lib/reading';
	import { AlertCircle, BookOpen, Bookmark, Pin, Search, Eye, Star, X } from '@lucide/svelte';
	import KindBadge from '$lib/components/KindBadge.svelte';
	import NsfwBadge from '$lib/components/NsfwBadge.svelte';
	import type { Paged, SavedSearch, SearchHit } from '$lib/types';
	import { confirmDialog, promptDialog } from '$lib/dialog.svelte';
	import { invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';
	import { activeFilterCount, resultsPath, type SearchFilters } from '$lib/searchParams';

	let { data }: { data: PageData } = $props();

	let term = $state(data.q);
	let hits = $state<SearchHit[]>(data.results.items);
	let nextCursor = $state<string | null>(data.results.nextCursor);
	let total = $state<number | null>(data.results.total ?? null);
	let loadingMore = $state(false);

	// The server load is the source of truth after a navigation; re-sync when it changes.
	$effect(() => {
		hits = data.results.items;
		nextCursor = data.results.nextCursor;
		total = data.results.total ?? null;
		term = data.q;
	});

	const statuses = [
		{ value: '', label: 'All' },
		{ value: 'unwatched', label: 'Unwatched' },
		{ value: 'watched', label: 'Watched' }
	];

	/** The search state lives in the URL, so a result list is a link someone can keep. */
	function navigate(changes: Record<string, string>) {
		const params = new URLSearchParams();
		const next = {
			q: term,
			host: data.host,
			status: data.status,
			finished: data.finished,
			broken: data.broken,
			sort: data.sort,
			kind: data.kind,
			maxMinutes: data.maxMinutes,
			...changes
		};
		for (const [key, value] of Object.entries(next)) {
			if (value) params.set(key, value);
		}
		goto(`/search?${params}`, { keepFocus: true, noScroll: true });
	}

	/** The filters this page was loaded with — what "Load more" has to keep asking for. */
	const filters = $derived<SearchFilters>({
		q: data.q,
		host: data.host,
		status: data.status,
		finished: data.finished,
		broken: data.broken,
		sort: data.sort,
		kind: data.kind,
		maxMinutes: data.maxMinutes,
		itemTags: data.itemTags
	});

	// One answer to "is anything narrowing this?", counted from the same filters the query is
	// built from. The save button and the empty state each used to keep their own list, and
	// both had gaps: a tag or "finished this week" hid the save button, and "videos only" with
	// no hits was greeted as though nothing had been asked yet.
	const canSave = $derived(activeFilterCount(filters) > 0);
	const hasFilters = $derived(canSave || !!data.savedId);

	/** The kinds worth offering as a filter. Image and Social exist but are rarely what is sought. */
	const kinds = [
		{ value: 'article', label: 'Articles' },
		{ value: 'video', label: 'Videos' },
		{ value: 'repository', label: 'Repos' },
		{ value: 'paper', label: 'Papers' },
		{ value: 'document', label: 'Documents' }
	];

	/** Saves the question, not the answer — so it keeps up with the collection. */
	async function saveSearch() {
		const name = await promptDialog('Name this search', data.q || 'Saved search');
		if (!name?.trim()) return;

		const res = await api.post('/search/saved', {
			name,
			q: data.q || null,
			host: data.host || null,
			itemTags: data.itemTags,
			status: data.status || null,
			broken: !!data.broken,
			sort: data.sort || null,
			kind: data.kind || null,
			maxMinutes: data.maxMinutes ? Number(data.maxMinutes) : null
		});
		if (res.ok) await invalidateAll();
	}

	function applySaved(saved: SavedSearch) {
		const params = new URLSearchParams();
		if (saved.q) params.set('q', saved.q);
		if (saved.host) params.set('host', saved.host);
		if (saved.status) params.set('status', saved.status);
		if (saved.broken) params.set('broken', '1');
		if (saved.sort) params.set('sort', saved.sort);
		if (saved.kind) params.set('kind', saved.kind);
		if (saved.maxMinutes) params.set('maxMinutes', String(saved.maxMinutes));
		for (const tag of saved.itemTags) params.append('itemTag', tag);
		goto(`/search?${params}`, { noScroll: true });
	}

	/** The saved search being shown, when the page was opened from the sidebar. */
	const openedSaved = $derived(
		data.savedId ? (data.saved.find((s) => s.id === data.savedId) ?? null) : null
	);

	let pinning = $state<string | null>(null);
	let pinError = $state<string | null>(null);

	/**
	 * Keeps a saved search in the sidebar, or takes it out.
	 *
	 * Capped on the API side, because each pinned search is a count query on a request the app
	 * layout makes on every navigation — so refusing the sixth is a budget, not a taste.
	 */
	async function togglePin(saved: SavedSearch) {
		pinning = saved.id;
		pinError = null;

		const res = await api.put(`/search/saved/${saved.id}/pinned`, { pinned: !saved.pinned });
		pinning = null;

		if (res.ok) {
			await invalidateAll();
		} else if (res.status === 400) {
			pinError = 'You can keep five searches in the sidebar. Take one out first.';
		} else {
			pinError = 'Could not change that.';
		}
	}

	async function forgetSaved(saved: SavedSearch) {
		const ok = await confirmDialog(`Forget "${saved.name}"? The links it finds are not affected.`, {
			confirmLabel: 'Forget'
		});
		if (!ok) return;

		const res = await api.del(`/search/saved/${saved.id}`);
		if (res.ok) await invalidateAll();
	}

	let debounce: ReturnType<typeof setTimeout>;
	function onInput() {
		clearTimeout(debounce);
		debounce = setTimeout(() => navigate({ q: term }), 300);
	}

	async function loadMore() {
		if (!nextCursor || loadingMore) return;
		loadingMore = true;
		try {
			// The same query the first page was asked with. A saved search pages by its id;
			// everything else carries every filter, not just the ones this used to remember.
			const res = await api.get(resultsPath(filters, data.savedId, { cursor: nextCursor }));
			if (res.ok) {
				const page = (await res.json()) as Paged<SearchHit>;
				hits = [...hits, ...page.items];
				nextCursor = page.nextCursor;
			}
		} finally {
			loadingMore = false;
		}
	}
</script>

<section class="mx-auto max-w-4xl">
	<h1 class="text-2xl font-semibold">{openedSaved?.name ?? 'Search'}</h1>
	<p class="mt-1 text-sm" style="color: var(--color-muted)">
		{#if openedSaved}
			<!-- Said plainly, because the boxes and chips below show the empty search rather than
			     this one — the saved search is run by id, not unpacked into them. -->
			A saved search, run just now. <a href="/search" class="underline underline-offset-2">Start a new one</a>
		{:else}
			Across every playlist you own — titles, descriptions, notes, addresses and the article text.
		{/if}
	</p>

	<Input
		icon={Search}
		class="mt-5"
		type="search"
		bind:value={term}
		oninput={onInput}
		placeholder="Search your links, or try site:bbc.co.uk under:10"
		aria-label="Search your links"
	/>

	<details class="mt-2">
		<summary class="cursor-pointer text-xs" style="color: var(--color-muted)">
			Things you can type
		</summary>
		<!-- The same filters as the buttons below, typed. Which matters because a typed search is
		     one you can put in a URL, send to somebody, or save as a sentence. -->
		<ul class="mt-2 grid gap-1 text-xs sm:grid-cols-2" style="color: var(--color-muted)">
			<li><code>site:bbc.co.uk</code> — only that site</li>
			<li><code>tag:rust</code> — only links you tagged that</li>
			<li><code>is:unread</code>, <code>is:read</code> — where you got to</li>
			<li><code>is:broken</code> — links whose page has gone</li>
			<li><code>is:highlighted</code> — articles you marked a passage in</li>
			<li><code>kind:video</code> — article, video, paper, audio…</li>
			<li><code>under:10</code> — readable in ten minutes</li>
			<li><code>score:&gt;80</code> — at least that well rated</li>
			<li><code>"exact phrase"</code> and <code>-exclude</code></li>
		</ul>
	</details>

	<div class="mt-3 flex flex-wrap items-center gap-2 text-sm">
		<SegmentedControl
			label="Status"
			options={statuses}
			value={data.status}
			onchange={(status) => navigate({ status })}
		/>

		<button
			type="button"
			onclick={() => navigate({ finished: data.finished ? '' : '7', status: '' })}
			class="rounded-md border px-3 py-1.5"
			class:font-medium={!!data.finished}
			style="border-color: {data.finished ? 'var(--color-accent)' : 'var(--color-border)'};
			       color: {data.finished ? 'var(--color-accent)' : 'inherit'}"
			title="Items you marked watched in the last week"
		>Finished this week</button>

		<button
			type="button"
			onclick={() => navigate({ sort: data.sort === 'score' ? '' : 'score' })}
			class="rounded-md border px-3 py-1.5"
			class:font-medium={data.sort === 'score'}
			style="border-color: {data.sort === 'score' ? 'var(--color-accent)' : 'var(--color-border)'};
			       color: {data.sort === 'score' ? 'var(--color-accent)' : 'inherit'}"
			title="Your highest-scored links, across every playlist"
		>Best rated</button>

		<Select
			value={data.kind}
			onchange={(e) => navigate({ kind: e.currentTarget.value })}
			aria-label="Kind"
			size="sm"
			class="w-auto"
			style={data.kind ? 'border-color: var(--color-accent); color: var(--color-accent)' : ''}
		>
			<option value="">Anything</option>
			{#each kinds as option (option.value)}
				<option value={option.value}>{option.label}</option>
			{/each}
		</Select>

		<!-- The question people actually ask when picking what to open: not "what is good", but
		     "what fits in the time I have". -->
		<button
			type="button"
			onclick={() => navigate({ maxMinutes: data.maxMinutes ? '' : '5' })}
			class="rounded-md border px-3 py-1.5"
			class:font-medium={!!data.maxMinutes}
			style="border-color: {data.maxMinutes ? 'var(--color-accent)' : 'var(--color-border)'};
			       color: {data.maxMinutes ? 'var(--color-accent)' : 'inherit'}"
			title="Articles you could finish in five minutes"
		>Under 5 min</button>

		<button
			type="button"
			onclick={() => navigate({ broken: data.broken ? '' : '1' })}
			class="rounded-md border px-3 py-1.5"
			class:font-medium={!!data.broken}
			style="border-color: {data.broken ? 'var(--color-danger)' : 'var(--color-border)'};
			       color: {data.broken ? 'var(--color-danger)' : 'inherit'}"
			title="Links whose page is gone or can no longer be read"
		>Broken</button>

		{#if data.host}
			<button
				type="button"
				onclick={() => navigate({ host: '' })}
				class="inline-flex items-center gap-1 rounded-md border px-2.5 py-1.5"
				style="border-color: var(--color-accent); color: var(--color-accent)"
			>
				{data.host}
				<X size={13} aria-hidden="true" />
			</button>
		{/if}

		{#if total !== null}
			<span class="ml-auto tabular-nums" style="color: var(--color-muted)">
				{total} {total === 1 ? 'result' : 'results'}
			</span>
		{/if}
	</div>

	{#if data.saved.length || canSave}
		<div class="mt-3 flex flex-wrap items-center gap-1.5">
			{#if pinError}
				<span class="text-xs" style="color: var(--color-danger)" role="alert">{pinError}</span>
			{/if}
			{#each data.saved as saved (saved.id)}
				<span
					class="inline-flex items-center rounded-md border text-xs"
					style="border-color: var(--color-border)"
				>
					<button type="button" onclick={() => applySaved(saved)} class="px-2 py-1 hover:underline">
						<Bookmark size={11} aria-hidden="true" class="mr-1 inline" />{saved.name}
					</button>
					<button
						type="button"
						onclick={() => togglePin(saved)}
						disabled={pinning !== null}
						class="border-l px-1.5 py-1 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
						style="border-color: var(--color-border); color: {saved.pinned
							? 'var(--color-accent)'
							: 'var(--color-muted)'}"
						title={saved.pinned ? `Take ${saved.name} out of the sidebar` : `Keep ${saved.name} in the sidebar`}
						aria-pressed={saved.pinned ?? false}
					>
						<Pin size={11} aria-hidden="true" />
					</button>
					<button
						type="button"
						onclick={() => forgetSaved(saved)}
						class="border-l px-1.5 py-1 hover:bg-black/5 dark:hover:bg-white/10"
						style="border-color: var(--color-border); color: var(--color-muted)"
						title={`Forget ${saved.name}`}
						aria-label={`Forget ${saved.name}`}
					>
						<X size={11} aria-hidden="true" />
					</button>
				</span>
			{/each}

			{#if canSave}
				<button
					type="button"
					onclick={saveSearch}
					class="rounded-md border border-dashed px-2 py-1 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border); color: var(--color-muted)"
					title="Come back to this search later"
				>+ Save this search</button>
			{/if}
		</div>
	{/if}

	{#if data.hosts.length > 1 && !data.host}
		<div class="mt-3 flex flex-wrap gap-1.5">
			{#each data.hosts.slice(0, 8) as facet (facet.hostname)}
				<button
					type="button"
					onclick={() => navigate({ host: facet.hostname })}
					class="rounded-md border px-2 py-1 text-xs hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border); color: var(--color-muted)"
				>
					{facet.hostname}
					<span class="tabular-nums opacity-60">{facet.itemCount}</span>
				</button>
			{/each}
		</div>
	{/if}

	{#if hits.length === 0}
		<div class="mt-8 rounded-lg border border-dashed p-10 text-center" style="border-color: var(--color-border)">
			<p class="font-medium">
				{hasFilters ? 'Nothing matched.' : 'Search across everything you have saved.'}
			</p>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				{hasFilters
					? 'Try fewer words, or a different filter.'
					: 'Type above, or pick a site to browse what you saved from it.'}
			</p>
			{#if hasFilters}
				<a
					href="/search"
					class="mt-4 inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
				>
					<X size={14} aria-hidden="true" /> Clear filters
				</a>
			{/if}
		</div>
	{:else}
		<ul class="mt-5 flex flex-col divide-y rounded-lg border" style="border-color: var(--color-border)">
			{#each hits as hit (hit.itemId)}
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
						>
							{hit.link.title ?? hit.link.url}
						</a>
						{#if hit.link.nsfw}<span class="ml-1.5"><NsfwBadge /></span>{/if}
						<KindBadge kind={hit.link.kind} />

						{#if hit.note}
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">{hit.note}</p>
						{/if}

						{#if hit.snippet}
							<!-- Only present when nothing else on the row contains the word typed, which
							     otherwise makes the hit look like a mistake. -->
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">…{hit.snippet}…</p>
						{/if}

						<p class="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs" style="color: var(--color-muted)">
							<a href={`/playlists/${hit.playlistId}`} class="hover:underline">{hit.playlistName}</a>
							<span aria-hidden="true">·</span>
							<button type="button" onclick={() => navigate({ host: hit.link.host })} class="hover:underline">
								{hit.link.host}
							</button>
							{#if hit.link.enrichmentStatus === 'Broken' || hit.link.enrichmentStatus === 'Failed'}
								<span aria-hidden="true">·</span>
								<span
									class="inline-flex items-center gap-1"
									style="color: {hit.link.enrichmentStatus === 'Broken' ? 'var(--color-danger)' : 'inherit'}"
									title={hit.link.enrichmentError ?? undefined}
								>
									<AlertCircle size={12} aria-hidden="true" />
									{hit.link.enrichmentStatus === 'Broken' ? 'page gone' : 'unreadable'}
								</span>
							{/if}
							{#if hit.status === 'Watched'}
								<span aria-hidden="true">·</span>
								<span class="inline-flex items-center gap-1"><Eye size={12} aria-hidden="true" /> watched</span>
							{/if}
							{#if hit.score !== null}
								<span aria-hidden="true">·</span>
								<span class="inline-flex items-center gap-1 tabular-nums">
									<Star size={12} aria-hidden="true" /> {hit.score}
								</span>
							{/if}
							{#if hit.link.wordCount}
								<span aria-hidden="true">·</span>
								<a href={`/read/${hit.link.id}`} class="inline-flex items-center gap-1 hover:underline">
									<BookOpen size={12} aria-hidden="true" /> {readingLabel(hit.link.wordCount)} read
								</a>
							{/if}
						</p>
					</div>
				</li>
			{/each}
		</ul>

		{#if nextCursor}
			<button
				type="button"
				onclick={loadMore}
				disabled={loadingMore}
				class="mt-3 w-full rounded-md border py-2 text-sm hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				{loadingMore ? 'Loading…' : 'Load more'}
			</button>
		{/if}
	{/if}
</section>
