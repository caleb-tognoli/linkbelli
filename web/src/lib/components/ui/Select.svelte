<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { HTMLSelectAttributes } from 'svelte/elements';
	import { controlClass } from './Input.svelte';

	/** A native select with the shared control look. Options go inside, as usual. */
	let {
		value = $bindable(),
		size = 'md',
		invalid = false,
		class: extra = '',
		children,
		...rest
	}: Omit<HTMLSelectAttributes, 'size' | 'class'> & {
		size?: 'sm' | 'md';
		invalid?: boolean;
		class?: string;
		children: Snippet;
	} = $props();
</script>

<select
	bind:value
	aria-invalid={invalid || undefined}
	class={controlClass(size, invalid, extra)}
	{...rest}
>
	{@render children()}
</select>
