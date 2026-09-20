<script lang="ts">
	import { Eye, EyeOff } from '@lucide/svelte';
	import type { HTMLInputAttributes } from 'svelte/elements';
	import { controlClass } from './Input.svelte';

	/**
	 * A password field that can be shown.
	 *
	 * Typing a password with the requirements "an upper-case letter, a number and a symbol" into a
	 * row of dots is how a typo gets locked into a new account. The eye shows it; showing it again
	 * hides it.
	 */
	let {
		value = $bindable(''),
		invalid = false,
		class: extra = '',
		...rest
	}: Omit<HTMLInputAttributes, 'type' | 'class' | 'size'> & { invalid?: boolean; class?: string } =
		$props();

	let shown = $state(false);
</script>

<div class="relative min-w-0 {extra}">
	<input
		{...rest}
		bind:value
		type={shown ? 'text' : 'password'}
		aria-invalid={invalid || undefined}
		class={controlClass('md', invalid, 'pr-10')}
	/>
	<button
		type="button"
		onclick={() => (shown = !shown)}
		class="absolute top-1/2 right-1 inline-flex size-8 -translate-y-1/2 items-center justify-center rounded-control text-muted hover:text-text"
		aria-label={shown ? 'Hide password' : 'Show password'}
		aria-pressed={shown}
		title={shown ? 'Hide password' : 'Show password'}
	>
		{#if shown}
			<EyeOff size={16} aria-hidden="true" />
		{:else}
			<Eye size={16} aria-hidden="true" />
		{/if}
	</button>
</div>
