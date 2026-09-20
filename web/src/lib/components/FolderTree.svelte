<script lang="ts">
	import { page } from '$app/state';
	import { SvelteSet } from 'svelte/reactivity';
	import { ChevronDown, ChevronRight, Folder as FolderIcon, FolderOpen } from '@lucide/svelte';
	import { buildFolderTree, flatten, pathTo } from '$lib/folderTree';
	import type { Folder } from '$lib/types';

	let { folders }: { folders: Folder[] } = $props();

	const tree = $derived(buildFolderTree(folders));

	/** The folder being looked at, when the current page is one. */
	const currentId = $derived(
		page.url.pathname.startsWith('/folders/') ? page.url.pathname.split('/')[2] : null
	);

	let open = new SvelteSet<string>();

	// The branch leading to whatever is open stays expanded, so arriving at a nested folder
	// directly shows where it sits rather than a collapsed list it isn't in.
	$effect(() => {
		for (const ancestor of pathTo(folders, currentId)) {
			open.add(ancestor.id);
		}
	});

	const rows = $derived(flatten(tree, open));

	function toggle(id: string) {
		if (!open.delete(id)) open.add(id);
	}
</script>

{#if rows.length > 0}
	<!--
		A tree, said out loud.

		Depth was carried by padding alone, on sibling divs — so a folder nested three deep inside
		a ten-deep hierarchy was announced as one item in a flat list of names, with nothing to say
		where it sat or that it had anything under it. The rows stay flat in the markup, because
		the open ones are computed as a flat list, and the tree roles carry the shape instead.
	-->
	<nav aria-label="Folders" class="flex flex-col gap-0.5 text-sm">
		<ul role="tree" aria-label="Folders" class="flex flex-col gap-0.5">
		{#each rows as node (node.id)}
			{@const isOpen = open.has(node.id)}
			{@const isCurrent = node.id === currentId}
			{@const siblings = rows.filter((r) => r.depth === node.depth)}
			<li
				role="treeitem"
				aria-level={node.depth + 1}
				aria-setsize={siblings.length}
				aria-posinset={siblings.indexOf(node) + 1}
				aria-expanded={node.children.length > 0 ? isOpen : undefined}
				aria-selected={isCurrent}
				aria-current={isCurrent ? 'page' : undefined}
				class="flex items-center"
				style={`padding-left: ${node.depth * 0.85}rem`}
			>
				{#if node.children.length > 0}
					<button
						type="button"
						onclick={() => toggle(node.id)}
						class="inline-flex items-center justify-center rounded-control size-6 tap-target shrink-0 hover:bg-black/5 dark:hover:bg-white/10"
						style="color: var(--color-muted)"
						aria-expanded={isOpen}
						aria-label={isOpen ? `Collapse ${node.name}` : `Expand ${node.name}`}
					>
						{#if isOpen}
							<ChevronDown size={14} aria-hidden="true" />
						{:else}
							<ChevronRight size={14} aria-hidden="true" />
						{/if}
					</button>
				{:else}
					<!-- Keeps leaves aligned with their siblings that do have a toggle. -->
					<span class="inline-block shrink-0" style="width: 1.25rem"></span>
				{/if}

				<a
					href={`/folders/${node.id}`}
					class="flex min-w-0 flex-1 items-center gap-1.5 rounded-control px-1.5 py-1 hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={isCurrent}
					style={isCurrent ? 'background: var(--color-border)' : ''}
					aria-current={isCurrent ? 'page' : undefined}
				>
					{#if isOpen}
						<FolderOpen size={14} aria-hidden="true" style="color: var(--color-muted)" />
					{:else}
						<FolderIcon size={14} aria-hidden="true" style="color: var(--color-muted)" />
					{/if}
					<span class="truncate">{node.name}</span>
					{#if node.playlistCount > 0}
						<span class="ml-auto shrink-0 tabular-nums text-xs" style="color: var(--color-muted)">
							{node.playlistCount}
						</span>
					{/if}
				</a>
			</li>
		{/each}
		</ul>
	</nav>
{/if}
