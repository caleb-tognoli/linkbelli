<script lang="ts">
	import Badge from '$lib/components/ui/Badge.svelte';
	import { Dialog } from 'bits-ui';
	import { goto } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import { buildCommands } from '$lib/commands';
	import { isTypingTarget } from '$lib/keyboard';
	import { Search } from '@lucide/svelte';
	import type { Paged, Playlist } from '$lib/types';

	let open = $state(false);
	let query = $state('');
	let selected = $state(0);
	let playlists = $state<Playlist[]>([]);
	let loaded = false;

	const commands = $derived(buildCommands(query, playlists));

	// Clamped rather than reset: as the list narrows under typing, the selection should stay on
	// something real without jumping back to the top on every keystroke.
	const active = $derived(commands[Math.min(selected, Math.max(0, commands.length - 1))]);

	function onKeydown(event: KeyboardEvent) {
		// Ctrl/Cmd+K everywhere, and "/" only when nothing is being typed into — otherwise it
		// would hijack a slash in the middle of a note.
		if ((event.key === 'k' && (event.metaKey || event.ctrlKey)) ||
			(event.key === '/' && !isTypingTarget(event.target) && !event.metaKey && !event.ctrlKey)) {
			event.preventDefault();
			show();
		}
	}

	async function show() {
		open = true;
		query = '';
		selected = 0;

		// Fetched once per page load: the palette is opened repeatedly and the list rarely moves.
		if (loaded) return;
		try {
			const page = await api.get('/playlists?limit=100').then((r) => json<Paged<Playlist>>(r));
			playlists = page.items;
			loaded = true;
		} catch {
			// Places to go still work without them.
		}
	}

	function onInputKeydown(event: KeyboardEvent) {
		if (event.key === 'ArrowDown') {
			event.preventDefault();
			selected = Math.min(selected + 1, commands.length - 1);
		} else if (event.key === 'ArrowUp') {
			event.preventDefault();
			selected = Math.max(selected - 1, 0);
		} else if (event.key === 'Enter' && active) {
			event.preventDefault();
			run(active.href);
		}
	}

	// Ids for the combobox wiring: which list it controls, and which option is the active one.
	const uid = $props.id();
	const listId = `${uid}-list`;
	const optionId = (index: number) => `${uid}-option-${index}`;
	const activeIndex = $derived(Math.min(selected, Math.max(0, commands.length - 1)));

	// The list is taller than it shows; arrowing past the edge brings the active option into view.
	$effect(() => {
		if (!open || commands.length === 0) return;
		document.getElementById(optionId(activeIndex))?.scrollIntoView({ block: 'nearest' });
	});

	function run(href: string) {
		open = false;
		goto(href);
	}
</script>

<svelte:window onkeydown={onKeydown} />

<Dialog.Root bind:open>
	<Dialog.Portal>
		<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
		<Dialog.Content
			class="fixed left-1/2 top-[15vh] z-50 w-[92vw] max-w-lg -translate-x-1/2 overflow-hidden rounded-xl border shadow-2xl"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Title class="sr-only">Search and jump</Dialog.Title>

			<div class="flex items-center gap-2 border-b px-3" style="border-color: var(--color-border)">
				<Search size={16} aria-hidden="true" style="color: var(--color-muted)" />
				<!-- svelte-ignore a11y_autofocus -- a palette that needs a click first is no faster than a menu -->
				<input
					autofocus
					bind:value={query}
					oninput={() => (selected = 0)}
					onkeydown={onInputKeydown}
					placeholder="Jump to a playlist, search, or paste a link…"
					aria-label="Jump to a playlist, search, or paste a link"
					role="combobox"
					aria-expanded={commands.length > 0}
					aria-controls={listId}
					aria-autocomplete="list"
					aria-activedescendant={commands.length > 0 ? optionId(activeIndex) : undefined}
					class="w-full bg-transparent py-3 outline-none focus-visible:!outline-none"
				/>
			</div>

			{#if commands.length === 0}
				<p class="px-4 py-6 text-center text-sm" style="color: var(--color-muted)">Nothing matches.</p>
			{:else}
				<!-- A listbox the input controls: the options are chosen with the arrow keys and Enter
				     from the input, so they are not tab stops of their own. -->
				<ul id={listId} role="listbox" aria-label="Results" class="max-h-80 overflow-y-auto py-1">
					{#each commands as command, index (command.id)}
						<!-- svelte-ignore a11y_click_events_have_key_events -- the input handles the keys -->
						<li
							id={optionId(index)}
							role="option"
							aria-selected={active?.id === command.id}
							onclick={() => run(command.href)}
							onmouseenter={() => (selected = index)}
							class="flex w-full cursor-pointer items-center gap-2 px-4 py-2 text-left text-sm"
							style={active?.id === command.id ? 'background: var(--color-selected)' : ''}
						>
							<span class="min-w-0 flex-1 truncate">{command.label}</span>
							<Badge>{command.kind}</Badge>
						</li>
					{/each}
				</ul>
			{/if}
		</Dialog.Content>
	</Dialog.Portal>
</Dialog.Root>
