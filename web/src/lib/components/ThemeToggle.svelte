<script lang="ts">
	import { Sun, Moon, Monitor } from '@lucide/svelte';

	type Theme = 'light' | 'dark' | 'system';

	let { initial }: { initial: Theme } = $props();
	let theme = $state<Theme>(initial);

	const options: { value: Theme; label: string }[] = [
		{ value: 'light', label: 'Light' },
		{ value: 'dark', label: 'Dark' },
		{ value: 'system', label: 'System' }
	];

	function set(value: Theme) {
		theme = value;
		// Persist for SSR on next load and apply immediately (no reload).
		document.cookie = `lb_theme=${value}; path=/; max-age=31536000; samesite=lax`;
		document.documentElement.dataset.theme = value;
	}
</script>

<div class="inline-flex overflow-hidden rounded-md border" style="border-color: var(--color-border)">
	{#each options as opt (opt.value)}
		<button
			type="button"
			onclick={() => set(opt.value)}
			class="p-1.5"
			style={theme === opt.value
				? 'background: var(--color-accent); color: var(--color-accent-contrast)'
				: 'color: var(--color-muted)'}
			aria-pressed={theme === opt.value}
			aria-label={opt.label}
			title={opt.label}
		>
			{#if opt.value === 'light'}<Sun size={18} aria-hidden="true" />{/if}
			{#if opt.value === 'dark'}<Moon size={18} aria-hidden="true" />{/if}
			{#if opt.value === 'system'}<Monitor size={18} aria-hidden="true" />{/if}
		</button>
	{/each}
</div>
