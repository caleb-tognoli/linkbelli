<svelte:head><title>Sources - linkbelli</title></svelte:head>

<script lang="ts">
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import { Plus } from '@lucide/svelte';
	import SourcesList from '$lib/components/SourcesList.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<Page>
	<PageHeader title="Sources">
		{#snippet actions()}
			<a
				href="/sources/new"
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="New source"
				aria-label="New source"
			>
				<Plus size={18} aria-hidden="true" />
			</a>
		{/snippet}
	</PageHeader>

	<div class="mt-6">
		<SourcesList sources={data.sources} />
	</div>
</Page>
