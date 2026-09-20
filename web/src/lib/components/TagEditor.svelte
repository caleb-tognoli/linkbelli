<script lang="ts">
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import Chip from '$lib/components/ui/Chip.svelte';
	import { api } from '$lib/api/client';
	import { Tag } from '@lucide/svelte';
	import type { Playlist, TagSummary } from '$lib/types';

	let {
		playlistId,
		tags = $bindable(),
		readonly = false
	}: { playlistId: string; tags: string[]; readonly?: boolean } = $props();

	let input = $state('');
	let busy = $state(false);
	let focused = $state(false);
	let element = $state<HTMLInputElement>();

	/**
	 * Tags this person already uses, offered as they type.
	 *
	 * Typed blind, "ai", "AI" and "a.i." become three tags with one playlist each. Fetched once,
	 * the first time somebody actually puts the cursor in the box.
	 */
	let known = $state<TagSummary[] | null>(null);
	let highlighted = $state(0);

	async function loadKnown() {
		if (known !== null) return;
		known = [];
		try {
			const res = await api.get('/tags');
			if (res.ok) known = (await res.json()) as TagSummary[];
		} catch {
			// Typing them out still works.
		}
	}

	const suggestions = $derived.by(() => {
		const term = input.trim().toLowerCase();
		if (!term || !known) return [];
		return known
			.filter((t) => t.name.toLowerCase().includes(term) && !tags.includes(t.name))
			.slice(0, 6);
	});

	// Never points past the end of a list that narrows as the word grows.
	$effect(() => {
		if (highlighted >= suggestions.length) highlighted = 0;
	});

	async function save(next: string[]) {
		const before = tags;
		busy = true;
		try {
			const res = await api.patch(`/playlists/${playlistId}`, { tags: next });
			if (res.ok) {
				tags = ((await res.json()) as Playlist).tags;
			} else {
				// Put back what was on the page: a tag that looks added and is not is worse than
				// one that visibly failed to be.
				tags = before;
				toast.error(failureMessage(res, 'Could not save the tags.'));
			}
		} finally {
			busy = false;
		}
	}

	/**
	 * Adds what has been typed.
	 *
	 * Called from Enter, from a comma, and from picking a suggestion — but never from blur. It
	 * used to be: tabbing out of a half-typed word filed it as a tag, which is how a library
	 * collects "jav" and "rus".
	 */
	function addTag(name = input) {
		const t = name.trim().replace(/,+$/, '').toLowerCase();
		input = '';
		if (!t || tags.some((existing) => existing.toLowerCase() === t)) return;
		void save([...tags, t]);
	}

	function removeTag(tag: string) {
		void save(tags.filter((t) => t !== tag));
	}

	function onkeydown(event: KeyboardEvent) {
		if (event.key === 'Enter' || event.key === ',') {
			event.preventDefault();
			addTag(suggestions[highlighted] && event.key === 'Enter' ? suggestions[highlighted].name : input);
		} else if (event.key === 'Escape' && input) {
			// Only the draft: Escape with an empty box belongs to whatever is around this.
			event.stopPropagation();
			input = '';
		} else if (event.key === 'ArrowDown' && suggestions.length > 0) {
			event.preventDefault();
			highlighted = (highlighted + 1) % suggestions.length;
		} else if (event.key === 'ArrowUp' && suggestions.length > 0) {
			event.preventDefault();
			highlighted = (highlighted - 1 + suggestions.length) % suggestions.length;
		}
	}

	const uid = $props.id();
	const listId = `${uid}-tags`;
</script>

<div class="flex flex-wrap items-center gap-1.5">
	{#each tags as tag (tag)}
		<Chip
			onremove={readonly ? undefined : () => removeTag(tag)}
			removeLabel={`Remove tag ${tag}`}
			disabled={busy}
		>
			{tag}
		</Chip>
	{/each}
	{#if !readonly}
		<div class="relative">
			<!-- Dashed while something is typed and not yet added, so a draft is visibly a draft
			     rather than something already saved. -->
			<span
				class="inline-flex min-h-6 items-center gap-1 rounded-full px-2 text-muted {input.trim()
					? 'border border-dashed border-border-strong'
					: ''}"
			>
				<Tag size={14} aria-hidden="true" />
				<input
					bind:this={element}
					bind:value={input}
					placeholder="Add tag"
					aria-label="Add tag"
					role="combobox"
					aria-expanded={suggestions.length > 0}
					aria-controls={listId}
					aria-activedescendant={suggestions.length > 0 ? `${listId}-${highlighted}` : undefined}
					autocomplete="off"
					disabled={busy}
					class="min-h-6 w-24 border-0 bg-transparent py-0.5 text-xs text-text outline-none"
					onfocus={() => {
						focused = true;
						void loadKnown();
					}}
					onblur={() => (focused = false)}
					{onkeydown}
				/>
			</span>
			{#if focused && suggestions.length > 0}
				<ul
					id={listId}
					role="listbox"
					aria-label="Tags you already use"
					class="popover-surface absolute top-full left-0 z-(--z-popover) mt-1 w-48 rounded-card border p-1 shadow-popover"
				>
					{#each suggestions as suggestion, i (suggestion.name)}
						<li
							id={`${listId}-${i}`}
							role="option"
							aria-selected={i === highlighted}
							class="flex items-center justify-between gap-2 rounded-control px-2 py-1 text-xs {i === highlighted
								? 'bg-selected text-accent'
								: ''}"
						>
							<!-- Pointer down, not click: a click would land after the blur that closes
							     this list. -->
							<button
								type="button"
								class="min-w-0 flex-1 truncate text-left"
								onpointerdown={(e) => {
									e.preventDefault();
									addTag(suggestion.name);
									element?.focus();
								}}
							>
								{suggestion.name}
							</button>
							<span class="shrink-0 tabular-nums text-muted">{suggestion.playlistCount}</span>
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	{/if}
</div>
