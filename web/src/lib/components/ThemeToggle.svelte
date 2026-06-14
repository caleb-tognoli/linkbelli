<script lang="ts">
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
			class="px-3 py-1.5 text-sm"
			style={theme === opt.value
				? 'background: var(--color-accent); color: var(--color-accent-contrast)'
				: 'color: var(--color-muted)'}
			aria-pressed={theme === opt.value}
		>
			{opt.label}
		</button>
	{/each}
</div>
