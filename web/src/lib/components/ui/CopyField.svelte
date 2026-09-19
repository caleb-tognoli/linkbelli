<script lang="ts">
	import { Check, Copy } from '@lucide/svelte';
	import Button from './Button.svelte';
	import { toast } from '$lib/toast.svelte';

	/**
	 * Something to copy — a link, a key, a secret — with a button that copies it.
	 *
	 * Written five times over, and each time a refused clipboard (an insecure origin, a declined
	 * permission) went unexplained. The value is always on screen and selected on focus, so it can
	 * be copied by hand; when the button cannot do it, a toast says so.
	 */
	let {
		value,
		label,
		copyLabel = 'Copy',
		mono = true,
		class: extra = ''
	}: {
		value: string;
		/** What the value is, as the field's accessible name. */
		label: string;
		copyLabel?: string;
		mono?: boolean;
		class?: string;
	} = $props();

	let copied = $state(false);
	let timer: ReturnType<typeof setTimeout> | undefined;

	async function copy() {
		try {
			await navigator.clipboard.writeText(value);
			copied = true;
			clearTimeout(timer);
			timer = setTimeout(() => (copied = false), 2000);
		} catch {
			toast.error('Your browser would not let this page copy. Select the text and copy it yourself.');
		}
	}
</script>

<div class="flex min-w-0 items-center gap-2 {extra}">
	<input
		readonly
		{value}
		aria-label={label}
		onfocus={(e) => e.currentTarget.select()}
		class="w-full min-w-0 flex-1 rounded-control border border-border-strong bg-bg px-2.5 py-1.5 text-xs {mono
			? 'font-mono'
			: ''}"
	/>
	<Button size="sm" icon={copied ? Check : Copy} onclick={copy}>{copied ? 'Copied' : copyLabel}</Button>
</div>
