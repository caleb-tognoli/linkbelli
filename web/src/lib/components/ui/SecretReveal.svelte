<script lang="ts">
	import { X } from '@lucide/svelte';
	import Button from './Button.svelte';
	import CopyField from './CopyField.svelte';

	/**
	 * A secret shown once — a new API key, a webhook's signing secret.
	 *
	 * Says so plainly, puts the value in a field that copies, and goes only when dismissed, so it
	 * cannot vanish while somebody is still reaching for it.
	 */
	let {
		title,
		value,
		label,
		ondismiss
	}: {
		/** What it is and what to do, e.g. "Copy your new key now — it won't be shown again." */
		title: string;
		value: string;
		/** The value's accessible name. */
		label: string;
		ondismiss: () => void;
	} = $props();
</script>

<div class="rounded-card border border-accent bg-selected p-3 text-sm" role="status">
	<div class="flex items-start gap-2">
		<p class="min-w-0 flex-1 font-medium">{title}</p>
		<Button variant="ghost" size="sm" icon={X} iconOnly label="Dismiss" onclick={ondismiss} class="-mt-1 -mr-1" />
	</div>
	<CopyField {value} {label} class="mt-2" />
</div>
