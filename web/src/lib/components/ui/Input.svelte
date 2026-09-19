<script lang="ts" module>
	/**
	 * The look every text control shares — inputs, selects and textareas — at one height, with a
	 * boundary that meets 3:1 and an accent edge while focused.
	 */
	export function controlClass(size: 'sm' | 'md' = 'md', invalid = false, extra = ''): string {
		return [
			'w-full min-w-0 rounded-control border bg-bg text-sm text-text placeholder:text-muted',
			'focus-visible:border-accent disabled:cursor-not-allowed disabled:opacity-60',
			invalid ? 'border-danger' : 'border-border-strong',
			size === 'sm' ? 'px-2.5 py-1.5' : 'px-3 py-2',
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
		icon: Icon,
		class: extra = '',
		element = $bindable(),
		...rest
	}: Omit<HTMLInputAttributes, 'size' | 'class'> & {
		size?: 'sm' | 'md';
		invalid?: boolean;
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
			class={controlClass(size, invalid, 'pl-9')}
			{...rest}
		/>
	</div>
{:else}
	<input
		bind:this={element}
		bind:value
		aria-invalid={invalid || undefined}
		class={controlClass(size, invalid, extra)}
		{...rest}
	/>
{/if}
