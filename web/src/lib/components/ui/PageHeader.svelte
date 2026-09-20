<script lang="ts">
	import type { Snippet } from 'svelte';

	/**
	 * A page's title, what the page is for, and what can be done from it.
	 *
	 * Each page laid these out by hand — title alone, title over a caption, title beside icons —
	 * with actions that were sometimes labelled buttons and sometimes bare icons. One layout now:
	 * the heading and its description on the left, actions on the right, wrapping under on a
	 * narrow screen.
	 */
	let {
		title,
		description,
		details,
		actions,
		class: extra = ''
	}: {
		title: string;
		/** One or two sentences on what the page is for. */
		description?: string;
		/** A description with markup in it, used instead of `description`. */
		details?: Snippet;
		/** Buttons for the page: the primary action last. */
		actions?: Snippet;
		class?: string;
	} = $props();
</script>

<header class="flex flex-wrap items-start justify-between gap-x-4 gap-y-3 {extra}">
	<div class="min-w-0 flex-1 basis-64">
		<h1 class="t-page">{title}</h1>
		{#if details}
			<p class="mt-1 max-w-prose text-sm text-muted">{@render details()}</p>
		{:else if description}
			<p class="mt-1 max-w-prose text-sm text-muted">{description}</p>
		{/if}
	</div>
	{#if actions}
		<div class="flex shrink-0 flex-wrap items-center gap-2">{@render actions()}</div>
	{/if}
</header>
