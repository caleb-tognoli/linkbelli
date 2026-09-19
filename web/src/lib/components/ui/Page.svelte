<script lang="ts" module>
	/**
	 * How wide a page's content runs, by what the page holds rather than picked per file:
	 * `narrow` for reading down a list, `medium` for lists with a little more on each row and for
	 * settings, `default` for grids of cards, `form` for a single form.
	 */
	export const PAGE_WIDTHS = {
		narrow: 'max-w-3xl',
		medium: 'max-w-4xl',
		default: 'max-w-5xl',
		form: 'max-w-2xl'
	} as const;
</script>

<script lang="ts">
	import type { Snippet } from 'svelte';

	let {
		width = 'default',
		class: extra = '',
		children
	}: { width?: keyof typeof PAGE_WIDTHS; class?: string; children: Snippet } = $props();
</script>

<section class="mx-auto w-full {PAGE_WIDTHS[width]} {extra}">
	{@render children()}
</section>
