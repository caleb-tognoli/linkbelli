<script lang="ts">
	import type { HTMLTextareaAttributes } from 'svelte/elements';
	import { controlClass, type ControlWidth } from './Input.svelte';

	/** A textarea with the shared control look. Resizes vertically only. */
	let {
		value = $bindable(),
		size = 'md',
		invalid = false,
		width = 'full',
		class: extra = '',
		element = $bindable(),
		...rest
	}: Omit<HTMLTextareaAttributes, 'class'> & {
		size?: 'sm' | 'md';
		invalid?: boolean;
		width?: ControlWidth;
		class?: string;
		element?: HTMLTextAreaElement;
	} = $props();
</script>

<textarea
	bind:this={element}
	bind:value
	aria-invalid={invalid || undefined}
	class={controlClass(size, invalid, `resize-y ${extra}`, width)}
	{...rest}
></textarea>
