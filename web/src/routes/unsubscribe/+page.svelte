<svelte:head><title>Unsubscribed - linkbelli</title></svelte:head>

<script lang="ts">
	import { Check, X } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6 text-center"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	{#if data.ok}
		<Check size={28} aria-hidden="true" class="mx-auto" style="color: var(--color-success)" />
		<h1 class="mt-2 text-xl font-semibold">Done</h1>
		<!-- Named, so somebody knows exactly what has stopped rather than fearing it was all of it. -->
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			You will no longer be emailed {data.description ?? 'about that'}.
		</p>
		<p class="mt-4 text-sm" style="color: var(--color-muted)">
			Everything else is unchanged. You can adjust the rest in
			<a href="/settings#email" class="underline underline-offset-2" style="color: var(--color-accent)">
				your email settings
			</a>.
		</p>
	{:else}
		<X size={28} aria-hidden="true" class="mx-auto" style="color: var(--color-muted)" />
		<h1 class="mt-2 text-xl font-semibold">That link did not work</h1>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Mail clients sometimes break long links. You can turn these off in your email settings instead.
		</p>
		<p class="mt-4 text-sm">
			<a href="/settings#email" class="underline underline-offset-2" style="color: var(--color-accent)">
				Open email settings
			</a>
		</p>
	{/if}
</div>
