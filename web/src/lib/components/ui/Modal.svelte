<script lang="ts" module>
	/** The footer row every modal uses: centred, with Cancel before the confirming action. */
	export const MODAL_FOOTER = 'mt-5 flex shrink-0 flex-wrap justify-center gap-2';
</script>

<script lang="ts">
	import { Dialog } from 'bits-ui';
	import type { Snippet } from 'svelte';
	import { X } from '@lucide/svelte';
	import { buttonClass } from './Button.svelte';

	/**
	 * The one dialog shell.
	 *
	 * Sixteen dialogs each built their own: widths from max-w-sm to max-w-lg, p-5 or p-6, a
	 * title in two sizes, a close button in some, a footer centred in some and left-aligned in
	 * others, and a height cap in only a few — so the longer ones ran off a short screen. This
	 * gives every dialog the same frame: a title and optional description wired to the dialog's
	 * accessible name, a close button, a body that scrolls inside the viewport, and an optional
	 * footer row.
	 *
	 * `trigger` renders inside the dialog root, so it can use Dialog.Trigger. A form whose submit
	 * button belongs in the footer puts the footer inside the form with MODAL_FOOTER instead.
	 */
	let {
		open = $bindable(false),
		title,
		description,
		size = 'md',
		onOpenChange,
		trigger,
		children,
		footer
	}: {
		open?: boolean;
		title: string;
		description?: string;
		size?: 'sm' | 'md' | 'lg';
		onOpenChange?: (open: boolean) => void;
		trigger?: Snippet;
		children: Snippet;
		footer?: Snippet;
	} = $props();

	const width = $derived({ sm: 'max-w-sm', md: 'max-w-md', lg: 'max-w-lg' }[size]);
</script>

<Dialog.Root bind:open {onOpenChange}>
	{#if trigger}{@render trigger()}{/if}
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-(--z-overlay) bg-black/40" />
		<Dialog.Content
			class="fixed top-1/2 left-1/2 z-(--z-modal) flex max-h-[85dvh] w-[calc(100vw-2rem)] {width} -translate-x-1/2 -translate-y-1/2 flex-col rounded-dialog border border-border bg-surface p-5 shadow-dialog"
		>
			<div class="flex shrink-0 items-start justify-between gap-3">
				<div class="min-w-0">
					<Dialog.Title class="text-lg font-semibold">{title}</Dialog.Title>
					{#if description}
						<Dialog.Description class="mt-1 text-sm text-muted">{description}</Dialog.Description>
					{/if}
				</div>
				<Dialog.Close class={buttonClass('ghost', 'md', true, '-mt-1 -mr-1')} title="Close" aria-label="Close">
					<X size={17} aria-hidden="true" />
				</Dialog.Close>
			</div>
			<!-- A little inset, so the focus ring of a field at the edge is not cut off by the
			     scrolling box. -->
			<div class="-mx-1 mt-4 min-h-0 flex-1 overflow-y-auto px-1 pb-1">
				{@render children()}
			</div>
			{#if footer}
				<div class={MODAL_FOOTER}>{@render footer()}</div>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
