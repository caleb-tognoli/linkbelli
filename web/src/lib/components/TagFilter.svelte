<script lang="ts">
	import Chip from '$lib/components/ui/Chip.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import { Popover } from 'bits-ui';
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { Tag } from '@lucide/svelte';
	import type { TagSummary } from '$lib/types';

	let {
		active,
		basePath,
		suggestPath,
		extraParams = {}
	}: {
		active: string[];
		basePath: string;
		suggestPath: string;
		extraParams?: Record<string, string>;
	} = $props();

	let open = $state(false);
	let query = $state('');
	let results = $state<TagSummary[]>([]);
	let loading = $state(false);

	function navigate(tags: string[]) {
		const params = new URLSearchParams();
		for (const [k, v] of Object.entries(extraParams)) if (v) params.set(k, v);
		for (const t of tags) params.append('tag', t);
		const qs = params.toString();
		goto(qs ? `${basePath}?${qs}` : basePath, { keepFocus: true });
	}

	function add(tag: string) {
		const t = tag.trim().toLowerCase();
		open = false;
		query = '';
		results = [];
		if (t && !active.includes(t)) navigate([...active, t]);
	}

	function remove(tag: string) {
		navigate(active.filter((x) => x !== tag));
	}

	async function search(term: string) {
		loading = true;
		try {
			const res = await api.get(`${suggestPath}?q=${encodeURIComponent(term.trim())}`);
			// Server returns tags ordered by popularity; just drop already-active ones.
			if (res.ok) results = ((await res.json()) as TagSummary[]).filter((t) => !active.includes(t.name));
		} finally {
			loading = false;
		}
	}

	// Refresh suggestions when the popover opens or the query changes. The term is passed
	// rather than read inside search(), so the dependency is something the code uses instead of
	// a bare `query;` statement that reads like a mistake.
	$effect(() => {
		const term = query;
		if (open) void search(term);
	});
</script>

<div class="flex flex-wrap items-center gap-2 text-sm">
	{#each active as tag (tag)}
		<Chip tone="accent" onremove={() => remove(tag)} removeLabel={`Remove ${tag} filter`}>{tag}</Chip>
	{/each}

	<Popover.Root bind:open>
		<Popover.Trigger
			class="inline-flex items-center gap-1.5 rounded-full border border-dashed px-2.5 py-1"
			style="border-color: var(--color-border); color: var(--color-muted)"
			title="Filter by tag"
			aria-label="Filter by tag"
		>
			<Tag size={15} aria-hidden="true" />
			Filter by tag
		</Popover.Trigger>
		<Popover.Portal>
			<Popover.Content
				class="popover-surface z-(--z-popover) w-64 rounded-lg border p-2 shadow-2xl"
				sideOffset={6}
			>
				<Input
					bind:value={query}
					placeholder="Search tags…"
					aria-label="Search tags"
					onkeydown={(e) => e.key === 'Enter' && add(query)}
					size="sm"
					class="w-full"
				/>
				<ul class="mt-2 max-h-60 overflow-auto">
					{#each results as t (t.name)}
						<li>
							<button
								type="button"
								onclick={() => add(t.name)}
								class="flex w-full items-center justify-between rounded px-2 py-1.5 text-left hover:bg-black/5 dark:hover:bg-white/10"
							>
								<span>{t.name}</span>
								<span class="text-xs" style="color: var(--color-muted)">{t.playlistCount}</span>
							</button>
						</li>
					{:else}
						<li class="px-2 py-1.5 text-sm" style="color: var(--color-muted)">
							{loading ? 'Searching…' : query.trim() ? 'No matching tags' : 'No tags yet'}
						</li>
					{/each}
				</ul>
			</Popover.Content>
		</Popover.Portal>
	</Popover.Root>
</div>
