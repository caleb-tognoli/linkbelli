<script lang="ts">
	import { DropdownMenu } from 'bits-ui';
	import type { Snippet } from 'svelte';

	/**
	 * A menu behind a button.
	 *
	 * The filters, sorts, visibility and export pickers were popovers holding plain buttons:
	 * no menu role, no arrow keys, and the chosen option shown only by slightly bolder text.
	 * This is bits-ui's dropdown menu — arrow keys and typeahead move through the items, Escape
	 * closes it and returns focus, and radio items say which one is checked. Fill it with
	 * MenuItem and MenuRadio.
	 */
	let {
		open = $bindable(false),
		label,
		title,
		triggerClass,
		align = 'start',
		side = 'bottom',
		width = 'min-w-44',
		trigger,
		children
	}: {
		open?: boolean;
		/** The trigger's accessible name, when its content does not say it. */
		label?: string;
		title?: string;
		triggerClass: string;
		align?: 'start' | 'center' | 'end';
		side?: 'top' | 'bottom' | 'left' | 'right';
		width?: string;
		trigger: Snippet;
		children: Snippet;
	} = $props();
</script>

<DropdownMenu.Root bind:open>
	<DropdownMenu.Trigger class={triggerClass} {title} aria-label={label}>
		{@render trigger()}
	</DropdownMenu.Trigger>
	<DropdownMenu.Portal>
		<!-- Colours come through a class: bits-ui drops `style` on floating content. -->
		<DropdownMenu.Content
			class="popover-surface z-(--z-popover) {width} max-w-[calc(100vw-1rem)] overflow-hidden rounded-card border py-1 shadow-popover"
			sideOffset={4}
			{align}
			{side}
		>
			{@render children()}
		</DropdownMenu.Content>
	</DropdownMenu.Portal>
</DropdownMenu.Root>
