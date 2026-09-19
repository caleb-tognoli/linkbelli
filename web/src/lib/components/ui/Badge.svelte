<script lang="ts">
	import type { Snippet } from 'svelte';

	/**
	 * A small label that says what something is — "NSFW", "admin", "worth checking".
	 *
	 * Not interactive. For a tag you can follow or remove, use Chip.
	 */
	let {
		tone = 'neutral',
		title,
		class: extra = '',
		children
	}: {
		tone?: 'neutral' | 'accent' | 'danger' | 'warning' | 'solid-danger';
		title?: string;
		class?: string;
		children: Snippet;
	} = $props();

	const TONES = {
		neutral: 'bg-chip text-text',
		accent: 'bg-selected text-accent',
		danger: 'bg-danger/12 text-danger',
		warning: 'bg-warning/15 text-warning',
		'solid-danger': 'bg-danger-solid text-on-solid'
	};
</script>

<span
	class="inline-flex shrink-0 items-center gap-1 rounded px-1.5 py-0.5 align-middle text-xs font-semibold {TONES[tone]} {extra}"
	{title}
>
	{@render children()}
</span>
