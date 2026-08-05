<svelte:head><title>Discover templates - linkbelli</title></svelte:head>

<script lang="ts">
	import { api } from '$lib/api/client';
	import { Bookmark, X } from '@lucide/svelte';
	import type { SourceTemplate } from '$lib/types';

	let q = $state('');
	let tagFilter = $state<string[]>([]);
	let tagInput = $state('');
	let results = $state<SourceTemplate[]>([]);
	let loading = $state(false);
	let savingId = $state<string | null>(null);

	let debounceTimer: ReturnType<typeof setTimeout>;

	$effect(() => {
		void q; void tagFilter;
		clearTimeout(debounceTimer);
		debounceTimer = setTimeout(load, 300);
		return () => clearTimeout(debounceTimer);
	});

	async function load() {
		loading = true;
		try {
			const params = new URLSearchParams();
			if (q) params.set('q', q);
			for (const tag of tagFilter) params.append('tags', tag);
			const res = await api.get(`/templates/discover?${params}`);
			if (res.ok) results = await res.json();
		} finally {
			loading = false;
		}
	}

	function addTagFilter(raw: string) {
		const t = raw.trim().toLowerCase();
		if (t && !tagFilter.includes(t)) tagFilter = [...tagFilter, t];
		tagInput = '';
	}

	async function toggleSave(t: SourceTemplate) {
		savingId = t.id;
		try {
			if (t.savedByMe) {
				await api.del(`/templates/${t.id}/save`);
				results = results.map(r => r.id === t.id ? { ...r, savedByMe: false } : r);
			} else {
				await api.post(`/templates/${t.id}/save`, {});
				results = results.map(r => r.id === t.id ? { ...r, savedByMe: true } : r);
			}
		} finally {
			savingId = null;
		}
	}
</script>

<section class="mx-auto max-w-5xl">
	<a href="/sources/new" class="text-sm" style="color: var(--color-muted)">← New source</a>
	<h1 class="mt-3 text-2xl font-semibold">Discover templates</h1>

	<div class="mt-5 flex flex-col gap-4">
		<!-- Search bar -->
		<div class="flex gap-3">
			<input
				bind:value={q}
				placeholder="Search templates…"
				class="flex-1 rounded-md border px-3 py-2 text-sm"
				style="border-color: var(--color-border); background: var(--color-bg)"
			/>
		</div>

		<!-- Tag filter chips -->
		<div class="flex flex-wrap gap-1.5">
			{#each tagFilter as tag (tag)}
				<span class="inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs" style="border-color: var(--color-accent); color: var(--color-accent)">
					{tag}
					<button type="button" onclick={() => (tagFilter = tagFilter.filter(t => t !== tag))} class="hover:opacity-70" title="Remove filter" aria-label="Remove filter {tag}">
						<X size={10} aria-hidden="true" />
					</button>
				</span>
			{/each}
			<input
				bind:value={tagInput}
				onkeydown={(e) => { if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); addTagFilter(tagInput); } }}
				onblur={() => tagInput.trim() && addTagFilter(tagInput)}
				placeholder="Filter by tag…"
				class="rounded-full border px-2.5 py-0.5 text-xs outline-none"
				style="border-color: var(--color-border); background: var(--color-bg)"
			/>
		</div>

		{#if loading}
			<p class="text-sm" style="color: var(--color-muted)">Loading…</p>
		{:else if results.length === 0}
			<p class="text-sm" style="color: var(--color-muted)">No templates found.</p>
		{:else}
			<div class="grid grid-cols-[repeat(auto-fill,minmax(220px,1fr))] gap-4">
				{#each results as t (t.id)}
					<div class="relative flex flex-col gap-2 rounded-lg border p-4 text-sm" style="border-color: var(--color-border); background: var(--color-surface)">
						<div class="flex items-start justify-between gap-2">
							<span class="font-medium">{t.name}</span>
							<button
								type="button"
								onclick={() => toggleSave(t)}
								disabled={savingId === t.id}
								class="shrink-0 inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10 disabled:opacity-50"
								title={t.savedByMe ? 'Unsave template' : 'Save template'}
								aria-label={t.savedByMe ? 'Unsave' : 'Save'}
								style={t.savedByMe ? 'color: var(--color-accent)' : 'color: var(--color-muted)'}
							>
								<Bookmark size={15} fill={t.savedByMe ? 'currentColor' : 'none'} aria-hidden="true" />
							</button>
						</div>
						{#if t.description}
							<p class="line-clamp-2 text-xs" style="color: var(--color-muted)">{t.description}</p>
						{/if}
						{#if t.tags.length > 0}
							<div class="flex flex-wrap gap-1">
								{#each t.tags as tag (tag)}
									<button
										type="button"
										onclick={() => !tagFilter.includes(tag) && (tagFilter = [...tagFilter, tag])}
										class="rounded-full border px-1.5 py-0.5 text-xs hover:border-[var(--color-accent)] hover:text-[var(--color-accent)]"
										style="border-color: var(--color-border); color: var(--color-muted)"
									>{tag}</button>
								{/each}
							</div>
						{/if}
						<span class="mt-auto text-xs" style="color: var(--color-muted)">{t.type}</span>
					</div>
				{/each}
			</div>
		{/if}
	</div>
</section>
