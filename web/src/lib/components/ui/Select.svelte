<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { HTMLSelectAttributes } from 'svelte/elements';
	import { controlClass, type ControlWidth } from './Input.svelte';

	/** A native select with the shared control look. Options go inside, as usual. */
	let {
		value = $bindable(),
		size = 'md',
		invalid = false,
		width = 'full',
		class: extra = '',
		children,
		...rest
	}: Omit<HTMLSelectAttributes, 'size' | 'class'> & {
		size?: 'sm' | 'md';
		invalid?: boolean;
		/** `auto` sizes to the chosen option, for a select sitting in a row of buttons. */
		width?: ControlWidth;
		class?: string;
		children: Snippet;
	} = $props();
</script>

<select
	bind:value
	aria-invalid={invalid || undefined}
	class={controlClass(size, invalid, extra, width)}
	{...rest}
>
	{@render children()}
</select>
