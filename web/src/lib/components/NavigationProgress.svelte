<script lang="ts">
	import { navigating } from '$app/state';

	/**
	 * A thin bar along the top while a page is on its way.
	 *
	 * Every page is loaded on the server, which asks the API for its data, so on a slow
	 * connection a click did nothing visible for as long as that took. The bar appears only once
	 * a navigation has taken longer than a moment, so quick ones do not flash it.
	 */
	const DELAY_MS = 150;

	let visible = $state(false);

	$effect(() => {
		if (!navigating.to) {
			visible = false;
			return;
		}
		const timer = setTimeout(() => (visible = true), DELAY_MS);
		return () => clearTimeout(timer);
	});
</script>

{#if visible}
	<div
		class="fixed inset-x-0 top-0 z-(--z-toast) h-0.5 overflow-hidden"
		role="progressbar"
		aria-label="Loading the page"
		aria-valuetext="Loading"
	>
		<div class="navigation-progress h-full w-1/3 bg-accent"></div>
	</div>
{/if}

<style>
	.navigation-progress {
		animation: travel 1.1s ease-in-out infinite;
	}

	@keyframes travel {
		from {
			transform: translateX(-100%);
		}
		to {
			transform: translateX(300%);
		}
	}
</style>
