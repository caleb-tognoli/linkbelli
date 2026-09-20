<script lang="ts" module>
	import { Code2, FileText, Headphones, Image as ImageIcon, MessageSquare, Play, ScrollText } from '@lucide/svelte';
	import type { ContentKind } from '$lib/types';

	// Article is deliberately absent: the reading time beside it already says so, and a badge
	// on every second row stops carrying information.
	const ICONS = {
		Video: { icon: Play, label: 'Video' },
		Repository: { icon: Code2, label: 'Repository' },
		Paper: { icon: ScrollText, label: 'Paper' },
		Document: { icon: FileText, label: 'Document' },
		Audio: { icon: Headphones, label: 'Audio' },
		Image: { icon: ImageIcon, label: 'Image' },
		Social: { icon: MessageSquare, label: 'Post' }
	} as const;

	/**
	 * Whether this kind has a badge at all.
	 *
	 * Asked by anywhere that needs to draw something else when it does not — a caller cannot tell
	 * from the outside that an article, or a kind this version has never heard of, renders nothing.
	 */
	export function hasKindBadge(kind: ContentKind | undefined): boolean {
		return !!kind && kind in ICONS;
	}
</script>

<script lang="ts">
	let { kind, size = 12 }: { kind: ContentKind | undefined; size?: number } = $props();

	const badge = $derived(hasKindBadge(kind) ? ICONS[kind as keyof typeof ICONS] : null);
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
