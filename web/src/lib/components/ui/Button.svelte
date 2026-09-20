<script lang="ts" module>
	export type ButtonVariant =
		| 'primary'
		| 'secondary'
		| 'ghost'
		| 'danger'
		| 'danger-outline'
		| 'ghost-danger';
	export type ButtonSize = 'sm' | 'md';

	const BASE =
		'inline-flex shrink-0 items-center justify-center gap-1.5 whitespace-nowrap rounded-control transition-colors disabled:cursor-not-allowed disabled:opacity-60 aria-disabled:cursor-not-allowed aria-disabled:opacity-60';

	const VARIANTS: Record<ButtonVariant, string> = {
		primary: 'bg-accent-solid font-medium text-on-solid hover:brightness-110',
		secondary: 'border border-border hover:bg-black/5 dark:hover:bg-white/10',
		ghost: 'hover:bg-black/5 dark:hover:bg-white/10',
		danger: 'bg-danger-solid font-medium text-on-solid hover:brightness-110',
		'danger-outline': 'border border-danger font-medium text-danger hover:bg-danger/10',
		'ghost-danger': 'text-danger hover:bg-danger/10'
	};

	/**
	 * One height per size, whatever is inside.
	 *
	 * In pixels rather than rem because the root size here is 18px: a toolbar of buttons — some
	 * with labels, some icon-only, some with a label that hides itself on a phone — came out at
	 * 26, 31 and 34 pixels tall side by side, which is what made the playlist header look like
	 * three toolbars stacked.
	 */
	const SIZES: Record<ButtonSize, { text: string; icon: string; px: number }> = {
		md: { text: 'min-h-[38px] px-3 py-2 text-sm', icon: 'min-h-[38px] min-w-[38px] p-1.5', px: 17 },
		sm: { text: 'min-h-[32px] px-2.5 py-1 text-sm', icon: 'min-h-[32px] min-w-[32px] p-1', px: 15 }
	};

	/** The classes a Button renders with, for the rare trigger that has to be another element. */
	export function buttonClass(
		variant: ButtonVariant = 'secondary',
		size: ButtonSize = 'md',
		iconOnly = false,
		extra = ''
	): string {
		return [BASE, VARIANTS[variant], iconOnly ? SIZES[size].icon : SIZES[size].text, extra]
			.filter(Boolean)
			.join(' ');
	}

	export function iconSize(size: ButtonSize = 'md'): number {
		return SIZES[size].px;
	}
</script>

<script lang="ts">
	import type { Component, Snippet } from 'svelte';
	import type { HTMLAnchorAttributes, HTMLButtonAttributes } from 'svelte/elements';
	import { LoaderCircle } from '@lucide/svelte';

	/**
	 * The one button.
	 *
	 * Every button used to be spelled out inline — 38 copies of the accent fill alone, in five
	 * paddings and three text sizes, some icon-only, some text-only, some both — so two buttons
	 * side by side rarely matched. Variants say what a button is for; size is the only other
	 * choice. An icon-only button must say what it does: `label` becomes its accessible name and
	 * its tooltip.
	 */
	type Props = {
		variant?: ButtonVariant;
		size?: ButtonSize;
		/** A Lucide icon, drawn before the text (or alone, with `iconOnly`). */
		icon?: Component<{ size?: number; 'aria-hidden'?: boolean | 'true' }>;
		iconOnly?: boolean;
		/** Required with `iconOnly`: the accessible name and the tooltip. */
		label?: string;
		/** Shows a spinner in place of the icon and marks the button busy. */
		loading?: boolean;
		/** Renders a link that looks like a button. */
		href?: string;
		class?: string;
		children?: Snippet;
	} & Omit<HTMLButtonAttributes, 'class' | 'children'> &
		Omit<HTMLAnchorAttributes, 'class' | 'children' | 'type'>;

	let {
		variant = 'secondary',
		size = 'md',
		icon: Icon,
		iconOnly = false,
		label,
		loading = false,
		href,
		class: extra = '',
		type = 'button',
		disabled,
		children,
		...rest
	}: Props = $props();

	const classes = $derived(buttonClass(variant, size, iconOnly, extra));
	const px = $derived(iconSize(size));
</script>

{#snippet content()}
	{#if loading}
		<LoaderCircle size={px} aria-hidden="true" class="animate-spin" />
	{:else if Icon}
		<Icon size={px} aria-hidden="true" />
	{/if}
	{#if !iconOnly && children}{@render children()}{/if}
{/snippet}

{#if href}
	<a
		{href}
		class={classes}
		aria-label={iconOnly ? label : undefined}
		title={iconOnly ? label : undefined}
		{...rest as HTMLAnchorAttributes}
	>
		{@render content()}
	</a>
{:else}
	<button
		{type}
		class={classes}
		disabled={disabled || loading}
		aria-busy={loading || undefined}
		aria-label={iconOnly ? label : undefined}
		title={iconOnly ? label : undefined}
		{...rest as HTMLButtonAttributes}
	>
		{@render content()}
	</button>
{/if}
