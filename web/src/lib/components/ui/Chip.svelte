<script lang="ts">
	import type { Snippet } from 'svelte';
	import { X } from '@lucide/svelte';

	/**
	 * A tag: something to follow (`href`), or to take away (`onremove`).
	 *
	 * Tags were drawn five different ways — muted text on a page-coloured block that vanished on
	 * a card, rounded or pill, with remove buttons around 16px across. One shape now, at least
	 * 24px tall, with a remove button that is a real target and says what it removes.
	 */
	let {
		href,
		title,
		onremove,
		removeLabel,
		disabled = false,
		tone = 'neutral',
		class: extra = '',
		children
	}: {
		href?: string;
		title?: string;
		onremove?: () => void;
		/** Required with `onremove`: what the × does, e.g. "Remove tag rust". */
		removeLabel?: string;
		/** Holds the remove button still while a change is on its way. */
		disabled?: boolean;
		/** `accent` marks an active filter. */
		tone?: 'neutral' | 'accent';
		class?: string;
		children: Snippet;
	} = $props();

	const base = $derived(
		`inline-flex min-h-6 max-w-full items-center gap-1 rounded-full px-2 text-xs ${
			tone === 'accent' ? 'border border-accent bg-selected text-accent' : 'bg-chip text-text'
		} ${extra}`
	);
</script>

{#if href}
	<a {href} {title} class="{base} hover:underline"><span class="truncate">{@render children()}</span></a>
{:else}
	<span class={base} {title}>
		<span class="truncate">{@render children()}</span>
		{#if onremove}
			<button
				type="button"
				onclick={onremove}
				{disabled}
				class="-my-1 -mr-1.5 inline-flex size-6 shrink-0 items-center justify-center rounded-full hover:bg-black/10 dark:hover:bg-white/20"
				title={removeLabel}
				aria-label={removeLabel}
			>
				<X size={12} aria-hidden="true" />
			</button>
		{/if}
	</span>
{/if}
