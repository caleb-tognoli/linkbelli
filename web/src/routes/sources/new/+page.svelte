<svelte:head><title>New source — Linkbelli</title></svelte:head>

<script lang="ts">
	import SourceForm from '$lib/components/SourceForm.svelte';
</script>

<section class="mx-auto max-w-4xl">
	<a href="/#sources" class="text-sm" style="color: var(--color-muted)">← Sources</a>
	<h1 class="mt-3 text-2xl font-semibold">New source</h1>
	<div class="mt-5">
		<SourceForm mode="create" />
	</div>
</section>
