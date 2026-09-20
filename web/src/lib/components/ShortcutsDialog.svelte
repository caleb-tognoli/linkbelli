<script lang="ts">
	import Modal from '$lib/components/ui/Modal.svelte';
	import { isPlainKey } from '$lib/keyboard';
	import { shortcuts } from '$lib/overlays.svelte';

	/**
	 * Every key the app listens for, in one place.
	 *
	 * They were documented in a footnote under the link table — hidden below `sm` — and in the
	 * reader's own settings panel, so the only way to learn them was to already know.
	 */
	const GROUPS: { heading: string; keys: { keys: string[]; does: string }[] }[] = [
		{
			heading: 'Anywhere',
			keys: [
				{ keys: ['Ctrl', 'K'], does: 'Search, or jump to a playlist' },
				{ keys: ['/'], does: 'The same, when nothing is being typed into' },
				{ keys: ['?'], does: 'This list' }
			]
		},
		{
			heading: 'In a list of links',
			keys: [
				{ keys: ['j'], does: 'Next link' },
				{ keys: ['k'], does: 'Previous link' },
				{ keys: ['o'], does: 'Open it' },
				{ keys: ['e'], does: 'Mark it done' },
				{ keys: ['x'], does: 'Select it' }
			]
		},
		{
			heading: 'In the reader',
			keys: [
				{ keys: ['j'], does: 'Down the page' },
				{ keys: ['k'], does: 'Up the page' },
				{ keys: ['n'], does: 'Next in the playlist' },
				{ keys: ['p'], does: 'Previous in the playlist' },
				{ keys: ['e'], does: 'Mark it done' },
				{ keys: ['h'], does: 'Highlight what is selected' },
				{ keys: ['Esc'], does: 'Close what is open, then go back' }
			]
		}
	];

	function onkeydown(event: KeyboardEvent) {
		// "?" is Shift+/ on most layouts, so isPlainKey's "nothing is focused" test is what keeps
		// this out of the middle of a note.
		if (event.key === '?' && isPlainKey(event)) {
			event.preventDefault();
			shortcuts.toggle();
		}
	}
</script>

<svelte:window {onkeydown} />

<Modal
	bind:open={shortcuts.open}
	title="Keyboard shortcuts"
	description="Keys work when nothing is being typed into."
	size="md"
>
	<div class="flex flex-col gap-5">
		{#each GROUPS as group (group.heading)}
			<div>
				<h3 class="t-subsection">{group.heading}</h3>
				<dl class="mt-2 flex flex-col gap-1.5 text-sm">
					{#each group.keys as row (row.does)}
						<div class="flex items-baseline justify-between gap-4">
							<dt class="flex shrink-0 items-baseline gap-1">
								{#each row.keys as key (key)}
									<kbd class="rounded-control border border-border bg-bg px-1.5 py-0.5 text-xs">{key}</kbd>
								{/each}
							</dt>
							<dd class="min-w-0 flex-1 text-right text-muted">{row.does}</dd>
						</div>
					{/each}
				</dl>
			</div>
		{/each}
	</div>
</Modal>
