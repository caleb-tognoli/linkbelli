<script lang="ts">
	import { Globe } from '@lucide/svelte';

	/**
	 * The small square beside a link: its site's icon, or a stand-in for one.
	 *
	 * Three lists drew the missing case as a bare grey square — which, eighteen pixels across with
	 * a soft corner at the left edge of a row, is the exact shape of an unchecked checkbox. The
	 * link table, ten pixels away, puts real checkboxes in that position, so rows with no favicon
	 * looked selectable and were not.
	 *
	 * A globe says "a page somewhere" and cannot be mistaken for a control.
	 */
	let {
		src,
		size = 4,
		class: extra = ''
	}: {
		src?: string | null;
		/** In Tailwind size units, so a caller can say size-4 or size-7 as it would inline. */
		size?: number;
		class?: string;
	} = $props();

	// An icon the site offers and the browser then cannot load is the same as not having one.
	let failed = $state(false);
	const box = $derived(`size-${size} shrink-0 ${extra}`);
</script>

{#if src && !failed}
	<img {src} alt="" class="{box} object-contain" loading="lazy" decoding="async" onerror={() => (failed = true)} />
{:else}
	<span class="{box} inline-flex items-center justify-center text-muted" aria-hidden="true">
		<Globe size={Math.round(size * 4.5)} />
	</span>
{/if}
