<script lang="ts">
	import { prefersReducedMotion } from '$lib/motion';

	/**
	 * Rows at the height the real ones will be, while they are on their way.
	 *
	 * Dialogs said "Loading…" in muted text and then replaced it with a list of a different
	 * height, so the panel jumped the moment it became useful. These hold the space instead, and
	 * say nothing — the list itself is the message.
	 */
	let {
		rows = 4,
		label = 'Loading…',
		class: extra = ''
	}: {
		rows?: number;
		/** What is on its way, for anyone who cannot see the blocks. */
		label?: string;
		class?: string;
	} = $props();

	// The shimmer is decoration; where it is not wanted the blocks simply sit there.
	const animate = $derived(!prefersReducedMotion());
</script>

<!-- The blocks are decoration and stay hidden. Something has to be said, though: a skeleton
     announces nothing, so a screen reader met silence and then a list that had appeared. -->
<p class="sr-only" role="status">{label}</p>
<div class="flex flex-col gap-1 {extra}" aria-hidden="true">
	{#each Array(rows) as _, i (i)}
		<div class="h-8 rounded-control bg-chip" class:animate-pulse={animate}></div>
	{/each}
</div>
