<svelte:head><title>Sources - linkbelli</title></svelte:head>

<script lang="ts">
	import { Plus } from '@lucide/svelte';
	import SourcesList from '$lib/components/SourcesList.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<section class="mx-auto max-w-5xl">
	<header class="flex items-center justify-between gap-2">
		<h1 class="text-2xl font-semibold">Sources</h1>
		<a
			href="/sources/new"
			class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
			title="New source"
			aria-label="New source"
		>
			<Plus size={18} aria-hidden="true" />
		</a>
	</header>

	<div class="mt-6">
		<SourcesList sources={data.sources} />
	</div>
</section>
