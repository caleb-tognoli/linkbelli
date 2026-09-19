<script lang="ts">
	import { Heart } from '@lucide/svelte';
	import type { Snippet } from 'svelte';
	import NsfwBadge from './NsfwBadge.svelte';
	import Chip from './ui/Chip.svelte';
	import VisibilityBadge from './ui/VisibilityBadge.svelte';
	import type { Visibility } from '$lib/types';

	/**
	 * A playlist as a card, wherever playlists are listed.
	 *
	 * There were four of these — your own, a public one, one inside a folder, and one shared with
	 * you — each a copy of the last with a line changed, so a fix to one never reached the others.
	 * One card now, told what it has to show. The whole card is a link (a stretched link on the
	 * name), and `actions` sit above it, so a button on the card never nests inside the link.
	 */
	let {
		href,
		name,
		description = null,
		tags = [],
		nsfw = false,
		coverLinkId = null,
		visibility,
		owner,
		role,
		itemCount,
		pendingCount = 0,
		likeCount = 0,
		actions
	}: {
		href: string;
		name: string;
		description?: string | null;
		tags?: string[];
		nsfw?: boolean;
		coverLinkId?: string | null;
		/** Shown for your own playlists. */
		visibility?: Visibility;
		/** Shown for somebody else's playlist. */
		owner?: string;
		/** What you may do on a playlist shared with you. */
		role?: string;
		itemCount: number;
		/** Links still being fetched, so the count does not just creep up on its own. */
		pendingCount?: number | null;
		likeCount?: number | null;
		actions?: Snippet;
	} = $props();
</script>

<div
	class="relative flex flex-col gap-2 rounded-card border border-border bg-surface p-4 transition-colors hover:border-accent has-[a:focus-visible]:outline-2 has-[a:focus-visible]:outline-offset-2 has-[a:focus-visible]:outline-accent"
>
	{#if coverLinkId}
		<!-- Served through the same proxy every other thumbnail uses, so a cover leaks no more than
		     the rows already do. -->
		<img
			src={`/api/v1/thumbnails/${coverLinkId}`}
			alt=""
			class="-mx-4 -mt-4 mb-1 h-28 w-[calc(100%+2rem)] rounded-t-[inherit] object-cover"
			loading="lazy"
			onerror={(e) => e.currentTarget.remove()}
		/>
	{/if}

	<div class="flex items-start justify-between gap-2">
		<a {href} class="min-w-0 font-medium after:absolute after:inset-0 after:rounded-[inherit] focus-visible:outline-none">
			{name}
		</a>
		{#if nsfw || actions}
			<span class="relative z-(--z-sticky) flex shrink-0 items-center gap-1.5">
				{#if nsfw}<NsfwBadge />{/if}
				{#if actions}{@render actions()}{/if}
			</span>
		{/if}
	</div>

	{#if description}
		<p class="line-clamp-2 text-sm text-muted">{description}</p>
	{/if}

	{#if tags.length}
		<div class="flex flex-wrap gap-1">
			{#each tags as tag (tag)}
				<Chip>{tag}</Chip>
			{/each}
		</div>
	{/if}

	<div class="mt-auto flex items-center justify-between gap-2 text-xs text-muted">
		{#if visibility}
			<VisibilityBadge {visibility} />
		{:else if owner}
			<span class="truncate">@{owner}</span>
		{/if}
		<span class="flex shrink-0 items-center gap-2">
			{#if likeCount}
				<span class="inline-flex items-center gap-1">
					<Heart size={11} aria-hidden="true" />
					{likeCount}
					<span class="sr-only">{likeCount === 1 ? 'like' : 'likes'}</span>
				</span>
			{/if}
			{#if role}<span>{role.toLowerCase()}</span><span aria-hidden="true">·</span>{/if}
			<span>
				{itemCount} {itemCount === 1 ? 'link' : 'links'}
				{#if pendingCount}
					<span title="Being fetched now">· +{pendingCount}</span>
				{/if}
			</span>
		</span>
	</div>
</div>
