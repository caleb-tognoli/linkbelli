<script lang="ts">
	import { prefersReducedMotion } from '$lib/motion';

	/**
	 * Rows at the height the real ones will be, while they are on their way.
	 *
	 * Dialogs said "Loading…" in muted text and then replaced it with a list of a different
	 * height, so the panel jumped the moment it became useful. These hold the space instead, and
	 * say nothing — the list itself is the message.
	 */
	let { rows = 4, class: extra = '' }: { rows?: number; class?: string } = $props();

	// The shimmer is decoration; where it is not wanted the blocks simply sit there.
	const animate = $derived(!prefersReducedMotion());
</script>

<div class="flex flex-col gap-1 {extra}" aria-hidden="true">
	{#each Array(rows) as _, i (i)}
		<div class="h-8 rounded-md bg-chip" class:animate-pulse={animate}></div>
	{/each}
</div>
