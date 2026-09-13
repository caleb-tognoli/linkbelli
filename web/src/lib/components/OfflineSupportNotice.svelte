<script lang="ts">
	import { shellCache } from '$lib/serviceWorker.svelte';
	import { CloudOff } from '@lucide/svelte';

	/**
	 * Says so when this browser will not keep a copy of the app.
	 *
	 * Shown only where the app promises to work offline, and only once it is known — `pending`
	 * says nothing, because a notice that flashes on every load and then withdraws itself is
	 * worse than no notice.
	 *
	 * The wording separates the two halves on purpose. Without the worker the app cannot be
	 * *opened* without a connection; the save queue is localStorage and is unaffected. Saying
	 * "offline saving is unavailable" would be the easy sentence and the wrong one.
	 */
	let {
		compact = false,
		detail = false
	}: {
		compact?: boolean;
		/**
		 * Whether to include the browser's own words.
		 *
		 * Off where somebody is trying to save a link — a raw TypeError on the share-sheet screen
		 * is noise in the one place speed matters. On in settings, where the person reading is the
		 * one who would act on it.
		 */
		detail?: boolean;
	} = $props();
</script>

{#if shellCache.unavailable}
	<p
		class="flex items-start gap-2 {compact ? 'text-xs' : 'text-sm'}"
		style="color: var(--color-muted)"
		role="status"
	>
		<CloudOff size={compact ? 14 : 16} class="mt-0.5 shrink-0" aria-hidden="true" />
		<span>
			This browser will not keep a copy of Linkbelli, so it needs a connection to open. A link
			saved from a tab that is already open is still kept until it can be sent.
			{#if detail && shellCache.reason}
				<span class="block opacity-75">{shellCache.reason}</span>
			{/if}
		</span>
	</p>
{/if}
