<script lang="ts" module>
	import type { Component } from 'svelte';

	export interface Segment<T extends string = string> {
		value: T;
		label: string;
		icon?: Component<{ size?: number; 'aria-hidden'?: boolean | 'true' }>;
		/** Shows only the icon; the label stays as the accessible name and tooltip. */
		iconOnly?: boolean;
		/** Makes the option a link — for choices that are addresses, like a sort in the URL. */
		href?: string;
	}
</script>

<script lang="ts" generics="T extends string">
	/**
	 * One choice out of a few, side by side.
	 *
	 * There were three unrelated versions of this — accent-filled (import destination, source
	 * sign-in), surface-and-bold (search status, discover sort, schedule units) and
	 * accent-outlined (theme, reader settings) — none of which told a screen reader which
	 * option was chosen. This is a radio group: arrow keys move the choice, only the chosen
	 * option is a tab stop, and it is announced as checked. With `href` options it is a set of
	 * links instead, the current one marked as the page.
	 */
	let {
		options,
		value = $bindable(),
		onchange,
		label,
		size = 'md',
		class: extra = ''
	}: {
		options: Segment<T>[];
		value: T;
		onchange?: (value: T) => void;
		/** What the choice is about, for assistive technology. */
		label: string;
		size?: 'sm' | 'md';
		class?: string;
	} = $props();

	let group: HTMLElement | undefined = $state();

	const isLinks = $derived(options.some((o) => o.href));
	const pad = $derived(size === 'sm' ? 'px-2.5 py-1 text-xs' : 'px-3 py-1.5 text-sm');

	function choose(next: T) {
		if (next === value) return;
		value = next;
		onchange?.(next);
	}

	function onkeydown(event: KeyboardEvent) {
		const index = options.findIndex((o) => o.value === value);
		const step: Record<string, number> = {
			ArrowRight: index + 1,
			ArrowDown: index + 1,
			ArrowLeft: index - 1,
			ArrowUp: index - 1,
			Home: 0,
			End: options.length - 1
		};
		if (!(event.key in step)) return;
		const next = (step[event.key] + options.length) % options.length;

		event.preventDefault();
		choose(options[next].value);
		group?.querySelectorAll<HTMLElement>('[role="radio"]')[next]?.focus();
	}
</script>

{#snippet content(option: Segment<T>)}
	{#if option.icon}
		<option.icon size={size === 'sm' ? 13 : 15} aria-hidden="true" />
	{/if}
	{#if !option.iconOnly}<span>{option.label}</span>{/if}
{/snippet}

{#if isLinks}
	<nav
		aria-label={label}
		class="inline-flex max-w-full divide-x overflow-x-auto rounded-control border border-border {extra}"
	>
		{#each options as option (option.value)}
			{@const active = option.value === value}
			<a
				href={option.href}
				class="inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap {pad} {active
					? 'bg-selected font-medium text-accent'
					: 'bg-bg hover:bg-black/5 dark:hover:bg-white/10'}"
				aria-current={active ? 'page' : undefined}
				title={option.iconOnly ? option.label : undefined}
				aria-label={option.iconOnly ? option.label : undefined}
			>
				{@render content(option)}
			</a>
		{/each}
	</nav>
{:else}
	<div
		bind:this={group}
		role="radiogroup"
		aria-label={label}
		tabindex="-1"
		{onkeydown}
		class="inline-flex max-w-full divide-x overflow-x-auto rounded-control border border-border {extra}"
	>
		{#each options as option (option.value)}
			{@const active = option.value === value}
			<button
				type="button"
				role="radio"
				aria-checked={active}
				tabindex={active ? 0 : -1}
				onclick={() => choose(option.value)}
				class="inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap {pad} {active
					? 'bg-selected font-medium text-accent'
					: 'bg-bg hover:bg-black/5 dark:hover:bg-white/10'}"
				title={option.iconOnly ? option.label : undefined}
				aria-label={option.iconOnly ? option.label : undefined}
			>
				{@render content(option)}
			</button>
		{/each}
	</div>
{/if}
