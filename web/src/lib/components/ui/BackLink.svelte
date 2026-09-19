<script lang="ts">
	import { ChevronLeft } from '@lucide/svelte';

	/**
	 * The way back up a level: "‹ Playlists".
	 *
	 * Written out by hand in each page as a text arrow, muted and without any hover, so it was
	 * easy to miss that it was a link at all. With `onclick` instead of `href` it is a button, for
	 * a step back inside one page.
	 */
	let {
		href,
		onclick,
		label,
		class: extra = ''
	}: { href?: string; onclick?: () => void; label: string; class?: string } = $props();

	const cls = $derived(
		`-ml-1 inline-flex items-center gap-0.5 rounded-control py-0.5 pr-1.5 text-sm text-muted hover:text-text hover:underline ${extra}`
	);
</script>

{#if href}
	<a {href} class={cls}><ChevronLeft size={16} aria-hidden="true" />{label}</a>
{:else}
	<button type="button" {onclick} class={cls}><ChevronLeft size={16} aria-hidden="true" />{label}</button>
{/if}
