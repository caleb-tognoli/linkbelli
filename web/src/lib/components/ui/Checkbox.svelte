<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { HTMLInputAttributes } from 'svelte/elements';

	/**
	 * A checkbox you can hit.
	 *
	 * The browser's own box is 13px and the row selectors in a playlist were exactly that — well
	 * under the 24px a pointer target needs, and hopeless on a phone. This keeps the native
	 * control (so the keyboard, forms and screen readers get a real checkbox) at a readable size,
	 * in the accent, inside a label whose padding makes the whole 24px — 44px on a touch screen —
	 * clickable. Use it for selections and form choices; a setting that applies at once is a
	 * Switch.
	 */
	let {
		checked = $bindable(false),
		indeterminate = false,
		label,
		class: extra = '',
		children,
		...rest
	}: Omit<HTMLInputAttributes, 'type' | 'class' | 'checked'> & {
		checked?: boolean;
		indeterminate?: boolean;
		/** The accessible name, when there is no visible text beside the box. */
		label?: string;
		class?: string;
		children?: Snippet;
	} = $props();
</script>

<label class="inline-flex cursor-pointer items-center gap-2 {extra}">
	<span class="inline-flex size-6 shrink-0 items-center justify-center pointer-coarse:size-11">
		<input
			type="checkbox"
			bind:checked
			{indeterminate}
			aria-label={children ? undefined : label}
			class="size-4.5 cursor-pointer accent-accent-solid"
			{...rest}
		/>
	</span>
	{#if children}{@render children()}{/if}
</label>
