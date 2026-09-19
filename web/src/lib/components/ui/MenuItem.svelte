<script lang="ts" module>
	/** The look of a row in a menu: highlighted by keyboard or pointer alike. */
	export const MENU_ROW =
		'flex w-full cursor-pointer select-none items-center gap-2 px-3 py-2 text-left text-sm outline-none data-disabled:cursor-not-allowed data-disabled:opacity-50 data-highlighted:bg-black/5 dark:data-highlighted:bg-white/10';
</script>

<script lang="ts">
	import { DropdownMenu } from 'bits-ui';
	import type { Component, Snippet } from 'svelte';

	/** One action in a Menu. With `href` it is a link (a download, say) rather than a button. */
	let {
		icon: Icon,
		danger = false,
		disabled = false,
		href,
		download = false,
		onselect,
		children
	}: {
		icon?: Component<{ size?: number; 'aria-hidden'?: boolean | 'true' }>;
		danger?: boolean;
		disabled?: boolean;
		href?: string;
		download?: boolean;
		onselect?: () => void;
		children: Snippet;
	} = $props();

	const rowClass = $derived(`${MENU_ROW} ${danger ? 'text-danger' : ''}`);
</script>

{#snippet body()}
	{#if Icon}
		<span class="inline-flex shrink-0" style={danger ? '' : 'color: var(--color-muted)'}>
			<Icon size={16} aria-hidden="true" />
		</span>
	{/if}
	<span class="min-w-0 flex-1">{@render children()}</span>
{/snippet}

{#if href}
	<DropdownMenu.Item {disabled} onSelect={onselect}>
		{#snippet child({ props })}
			<a {...props} {href} download={download || undefined} class={rowClass}>
				{@render body()}
			</a>
		{/snippet}
	</DropdownMenu.Item>
{:else}
	<DropdownMenu.Item class={rowClass} {disabled} onSelect={onselect}>
		{@render body()}
	</DropdownMenu.Item>
{/if}
