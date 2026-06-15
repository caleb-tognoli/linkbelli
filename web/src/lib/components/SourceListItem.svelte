<script lang="ts">
	import type { Snippet } from 'svelte';

	let {
		name,
		badge,
		subtitle,
		href,
		actions
	}: { name: string; badge?: string; subtitle?: string; href?: string; actions?: Snippet } = $props();
</script>

<li
	class="flex items-center justify-between gap-3 rounded-lg border p-3"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<div class="min-w-0">
		<div class="flex items-baseline gap-1.5">
			{#if href}
				<a {href} class="font-medium hover:underline">{name}</a>
			{:else}
				<span class="font-medium">{name}</span>
			{/if}
			{#if badge}
				<span class="text-xs" style="color: var(--color-muted)">· {badge}</span>
			{/if}
		</div>
		{#if subtitle}
			<div class="text-xs" style="color: var(--color-muted)">{subtitle}</div>
		{/if}
	</div>
	{#if actions}
		<div class="flex shrink-0 items-center gap-3 text-sm">{@render actions()}</div>
	{/if}
</li>
