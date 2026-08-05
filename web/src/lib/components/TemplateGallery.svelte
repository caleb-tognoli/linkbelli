<script lang="ts">
	import { Pencil, Plus, Compass, Rss, Code2, Braces } from '@lucide/svelte';
	import type { SourceTemplate, SourceType } from '$lib/types';

	let {
		templates,
		onselect,
		onadvanced
	}: {
		templates: SourceTemplate[];
		onselect: (t: SourceTemplate) => void;
		onadvanced: (type: SourceType) => void;
	} = $props();

	const mine = $derived(templates.filter(t => t.ownedByMe));
	const saved = $derived(templates.filter(t => !t.ownedByMe && t.savedByMe));
	const app = $derived(templates.filter(t => t.ownerId === null));

	const cardBase = 'flex flex-col gap-1 rounded-lg border p-4 text-left text-sm transition-colors hover:border-[var(--color-accent)]';
	const cardStyle = 'border-color: var(--color-border); background: var(--color-surface)';
</script>

<div class="flex flex-col gap-8">

	<!-- My templates -->
	<section>
		<h2 class="mb-3 text-sm font-medium" style="color: var(--color-muted)">My templates</h2>
		<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
			{#each mine as t (t.id)}
				<div class="relative">
					<button
						type="button"
						onclick={() => onselect(t)}
						class="{cardBase} w-full"
						style={cardStyle}
					>
						<span class="font-medium">{t.name}</span>
						{#if t.description}
							<span class="line-clamp-2 text-xs" style="color: var(--color-muted)">{t.description}</span>
						{/if}
					</button>
					<a
						href="/templates/{t.id}/edit"
						class="absolute right-2 top-2 inline-flex items-center rounded p-1 hover:bg-black/10 dark:hover:bg-white/10"
						title="Edit template"
						aria-label="Edit template"
						onclick={(e) => e.stopPropagation()}
					>
						<Pencil size={13} aria-hidden="true" style="color: var(--color-muted)" />
					</a>
				</div>
			{/each}

			<!-- New template card -->
			<a
				href="/templates/new"
				class="flex flex-col items-center justify-center gap-1.5 rounded-lg border border-dashed p-4 text-sm transition-colors hover:border-[var(--color-accent)]"
				style="border-color: var(--color-border); color: var(--color-muted)"
			>
				<Plus size={20} aria-hidden="true" />
				<span>New template</span>
			</a>
		</div>
	</section>

	<!-- Saved templates -->
	<section>
		<h2 class="mb-3 text-sm font-medium" style="color: var(--color-muted)">Saved</h2>
		<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
			{#each saved as t (t.id)}
				<button
					type="button"
					onclick={() => onselect(t)}
					class={cardBase}
					style={cardStyle}
				>
					<span class="font-medium">{t.name}</span>
					{#if t.description}
						<span class="line-clamp-2 text-xs" style="color: var(--color-muted)">{t.description}</span>
					{/if}
				</button>
			{/each}

			<!-- Discover card -->
			<a
				href="/templates/discover"
				class="flex flex-col items-center justify-center gap-1.5 rounded-lg border border-dashed p-4 text-sm transition-colors hover:border-[var(--color-accent)]"
				style="border-color: var(--color-border); color: var(--color-muted)"
			>
				<Compass size={20} aria-hidden="true" />
				<span>Discover templates</span>
			</a>
		</div>
	</section>

	<!-- App templates -->
	{#if app.length > 0}
		<section>
			<h2 class="mb-3 text-sm font-medium" style="color: var(--color-muted)">App templates</h2>
			<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
				{#each app as t (t.id)}
					<button
						type="button"
						onclick={() => onselect(t)}
						class={cardBase}
						style={cardStyle}
					>
						<span class="font-medium">{t.name}</span>
						{#if t.description}
							<span class="line-clamp-2 text-xs" style="color: var(--color-muted)">{t.description}</span>
						{/if}
					</button>
				{/each}
			</div>
		</section>
	{/if}

	<!-- Advanced -->
	<section>
		<h2 class="mb-3 text-sm font-medium" style="color: var(--color-muted)">Advanced</h2>
		<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
			<button type="button" onclick={() => onadvanced('Rss')} class={cardBase} style={cardStyle}>
				<Rss size={18} aria-hidden="true" style="color: var(--color-muted)" />
				<span class="font-medium">RSS / Atom</span>
				<span class="text-xs" style="color: var(--color-muted)">Subscribe to a feed</span>
			</button>
			<button type="button" onclick={() => onadvanced('Scraper')} class={cardBase} style={cardStyle}>
				<Code2 size={18} aria-hidden="true" style="color: var(--color-muted)" />
				<span class="font-medium">Web scraper</span>
				<span class="text-xs" style="color: var(--color-muted)">Scrape links from a page</span>
			</button>
			<button type="button" onclick={() => onadvanced('JsonApi')} class={cardBase} style={cardStyle}>
				<Braces size={18} aria-hidden="true" style="color: var(--color-muted)" />
				<span class="font-medium">JSON API</span>
				<span class="text-xs" style="color: var(--color-muted)">Fetch links from a JSON endpoint</span>
			</button>
		</div>
	</section>

</div>
