<script lang="ts">
	import { Code2, FileText, Headphones, Image as ImageIcon, MessageSquare, Play, ScrollText } from '@lucide/svelte';
	import type { ContentKind } from '$lib/types';

	let { kind, size = 12 }: { kind: ContentKind | undefined; size?: number } = $props();

	// Article is deliberately absent: the reading time beside it already says so, and a badge
	// on every second row stops carrying information.
	const icons = {
		Video: { icon: Play, label: 'Video' },
		Repository: { icon: Code2, label: 'Repository' },
		Paper: { icon: ScrollText, label: 'Paper' },
		Document: { icon: FileText, label: 'Document' },
		Audio: { icon: Headphones, label: 'Audio' },
		Image: { icon: ImageIcon, label: 'Image' },
		Social: { icon: MessageSquare, label: 'Post' }
	} as const;

	const badge = $derived(kind && kind in icons ? icons[kind as keyof typeof icons] : null);
</script>

{#if badge}
	<span
		class="ml-1.5 inline-flex items-center gap-1 align-middle text-xs"
		style="color: var(--color-muted)"
		title={badge.label}
	>
		<badge.icon size={size} aria-hidden="true" />
		<span class="sr-only">{badge.label}</span>
	</span>
{/if}
