<script lang="ts">
	import type { HTMLTextareaAttributes } from 'svelte/elements';
	import { controlClass } from './Input.svelte';

	/** A textarea with the shared control look. Resizes vertically only. */
	let {
		value = $bindable(),
		size = 'md',
		invalid = false,
		class: extra = '',
		element = $bindable(),
		...rest
	}: Omit<HTMLTextareaAttributes, 'class'> & {
		size?: 'sm' | 'md';
		invalid?: boolean;
		class?: string;
		element?: HTMLTextAreaElement;
	} = $props();
</script>

<textarea
	bind:this={element}
	bind:value
	aria-invalid={invalid || undefined}
	class={controlClass(size, invalid, `resize-y ${extra}`)}
	{...rest}
></textarea>
