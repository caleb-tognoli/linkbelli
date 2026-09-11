<svelte:head><title>New source - linkbelli</title></svelte:head>

<script lang="ts">
	import SourceForm from '$lib/components/SourceForm.svelte';
	import TemplatePicker from '$lib/components/TemplatePicker.svelte';

	// Templates first: writing a feed path or a set of selectors by hand is the steepest part of
	// setting a source up, and for anything common it has already been worked out.
	let byHand = $state(false);
</script>

<section class="mx-auto max-w-4xl">
	<a href="/sources" class="text-sm" style="color: var(--color-muted)">← Sources</a>
	<h1 class="mt-3 text-2xl font-semibold">New source</h1>
	<div class="mt-5">
		{#if byHand}
			<SourceForm mode="create" />
		{:else}
			<TemplatePicker onskip={() => (byHand = true)} />
		{/if}
	</div>
</section>
