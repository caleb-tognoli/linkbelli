<script lang="ts">
	import { Sun, Moon, Monitor } from '@lucide/svelte';

	type Theme = 'light' | 'dark' | 'system';

	let { initial }: { initial: Theme } = $props();
	let theme = $state<Theme>(initial);

	const options: { value: Theme; label: string; desc: string; Icon: typeof Sun }[] = [
		{ value: 'light', label: 'Light', desc: 'Always light', Icon: Sun },
		{ value: 'dark', label: 'Dark', desc: 'Always dark', Icon: Moon },
		{ value: 'system', label: 'System', desc: 'Follow OS setting', Icon: Monitor }
	];

	function set(value: Theme) {
		theme = value;
		document.cookie = `lb_theme=${value}; path=/; max-age=31536000; samesite=lax`;
		document.documentElement.dataset.theme = value;
	}
</script>

<div class="flex gap-2">
	{#each options as opt (opt.value)}
		{@const active = theme === opt.value}
		<button
			type="button"
			onclick={() => set(opt.value)}
			class="flex flex-1 items-center gap-2.5 rounded-lg border px-3 py-2.5 text-left text-sm transition-colors"
			style={active
				? 'border-color: var(--color-accent); color: var(--color-accent)'
				: 'border-color: var(--color-border)'}
			aria-pressed={active}
		>
			<opt.Icon size={18} aria-hidden="true" />
			<span class="font-medium">{opt.label}</span>
		</button>
	{/each}
</div>
