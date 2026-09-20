<script lang="ts" module>
	import { CONTROL_HEIGHT, type ControlSize } from './sizes';

	/** How much room a control takes: the width of whatever holds it, or of its own contents. */
	export type ControlWidth = 'full' | 'auto';

	/**
	 * The look every text control shares — inputs, selects and textareas — at one height, with a
	 * boundary that meets 3:1 and an accent edge while focused.
	 *
	 * Width is a parameter rather than a class to override. Tailwind decides between two
	 * conflicting utilities by their order in the stylesheet, not by their order in the class
	 * attribute — so a caller passing `class="w-auto"` was silently still getting `w-full`, which
	 * is how one select in a filter row came out 588px wide and wrapped the row onto three lines.
	 */
	export function controlClass(
		size: ControlSize = 'md',
		invalid = false,
		extra = '',
		width: ControlWidth = 'full',
		/** `auto` for a textarea, which is as tall as its rows say. */
		height: 'fixed' | 'auto' = 'fixed'
	): string {
		return [
			'min-w-0 rounded-control border bg-bg text-sm text-text placeholder:text-muted',
			width === 'full' ? 'w-full' : 'w-auto',
			// One height with everything else in a row, rather than whatever padding and
			// line-height happened to add up to.
			height === 'fixed' ? CONTROL_HEIGHT[size] : size === 'sm' ? 'py-1.5' : 'py-2',
			'focus-visible:border-accent disabled:cursor-not-allowed disabled:opacity-60',
			invalid ? 'border-danger' : 'border-border-strong',
			size === 'sm' ? 'px-2.5' : 'px-3',
			extra
		]
			.filter(Boolean)
			.join(' ');
	}
</script>

<script lang="ts">
	import type { Component } from 'svelte';
	import type { HTMLInputAttributes } from 'svelte/elements';

	/**
	 * A text input. `icon` draws a Lucide icon inside the left edge, for search boxes.
	 */
	let {
		value = $bindable(),
		size = 'md',
		invalid = false,
		width = 'full',
		icon: Icon,
		class: extra = '',
		element = $bindable(),
		...rest
	}: Omit<HTMLInputAttributes, 'size' | 'class'> & {
		size?: 'sm' | 'md';
		invalid?: boolean;
		/** `auto` sizes to the contents, for a control sitting in a row of buttons. */
		width?: ControlWidth;
		icon?: Component<{ size?: number; 'aria-hidden'?: boolean | 'true'; class?: string }>;
		class?: string;
		element?: HTMLInputElement;
	} = $props();
</script>

{#if Icon}
	<div class="relative min-w-0 {extra}">
		<span
			class="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2"
			style="color: var(--color-muted)"
		>
			<Icon size={15} aria-hidden="true" />
		</span>
		<input
			bind:this={element}
			bind:value
			aria-invalid={invalid || undefined}
			class={controlClass(size, invalid, 'pl-9', width)}
			{...rest}
		/>
	</div>
{:else}
	<input
		bind:this={element}
		bind:value
		aria-invalid={invalid || undefined}
		class={controlClass(size, invalid, extra, width)}
		{...rest}
	/>
{/if}
