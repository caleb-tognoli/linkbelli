<script lang="ts">
	import { EyeOff, Globe, Lock } from '@lucide/svelte';
	import type { Visibility } from '$lib/types';

	/** Who can see a playlist, as a small pill: Private, Unlisted or Public. */
	let { visibility }: { visibility: Visibility } = $props();

	const ICONS = { Private: Lock, Unlisted: EyeOff, Public: Globe } as const;
	const Icon = $derived(ICONS[visibility] ?? Lock);
</script>

<span class="inline-flex items-center gap-1 rounded-full border border-border px-2 py-0.5 leading-none">
	<Icon size={11} aria-hidden="true" />
	{visibility}
</span>
