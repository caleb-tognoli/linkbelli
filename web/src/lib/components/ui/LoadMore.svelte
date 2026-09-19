<script lang="ts">
	import Button from './Button.svelte';
	import { ChevronDown } from '@lucide/svelte';

	/**
	 * "There is more of this list."
	 *
	 * It was a lone chevron in a box in five places, the words "Load more" in two, and Previous /
	 * Next pages in one — and none said how much more there was. One control now, in words, with
	 * the count when the list knows it, busy while the next page is on its way.
	 */
	let {
		onclick,
		loading = false,
		remaining = null,
		label = 'Show more',
		class: extra = ''
	}: {
		onclick: () => void;
		loading?: boolean;
		/** How many are still to come, when the list knows. */
		remaining?: number | null;
		label?: string;
		class?: string;
	} = $props();
</script>

<div class="mt-3 flex justify-center {extra}">
	<Button icon={ChevronDown} {loading} {onclick}>
		{loading ? 'Loading…' : label}
		{#if remaining !== null && remaining > 0 && !loading}
			<span class="text-muted">({remaining} more)</span>
		{/if}
	</Button>
</div>
