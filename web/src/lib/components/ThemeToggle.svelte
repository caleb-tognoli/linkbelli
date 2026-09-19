<script lang="ts">
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import { Sun, Moon, Monitor } from '@lucide/svelte';

	type Theme = 'light' | 'dark' | 'system';

	let { initial }: { initial: Theme } = $props();
	let theme = $state<Theme>(initial);

	const options: { value: Theme; label: string; Icon: typeof Sun }[] = [
		{ value: 'light', label: 'Light', Icon: Sun },
		{ value: 'dark', label: 'Dark', Icon: Moon },
		{ value: 'system', label: 'System', Icon: Monitor }
	];

	function set(value: Theme) {
		theme = value;
		document.cookie = `lb_theme=${value}; path=/; max-age=31536000; samesite=lax`;
		document.documentElement.dataset.theme = value;

		// The browser bar follows, as the server would have set it on the next page load.
		const bar = { light: '#f7f7f5', dark: '#202020' };
		document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]').forEach((meta) => {
			const scheme = meta.media.includes('dark') ? 'dark' : 'light';
			meta.content = value === 'system' ? bar[scheme] : bar[value];
		});
	}
</script>

<SegmentedControl
	label="Theme"
	options={options.map((opt) => ({ value: opt.value, label: opt.label, icon: opt.Icon }))}
	value={theme}
	onchange={set}
/>
