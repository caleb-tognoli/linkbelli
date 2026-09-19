<svelte:head><title>Sources - linkbelli</title></svelte:head>

<script lang="ts">
	import Button from '$lib/components/ui/Button.svelte';
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
			<Button href="/sources/new" variant="primary" icon={Plus}>New source</Button>
		{/snippet}
	</PageHeader>

	<div class="mt-6">
		<SourcesList sources={data.sources} />
	</div>
</Page>
