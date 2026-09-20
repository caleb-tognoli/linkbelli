<svelte:head><title>{pageTitle('New source')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import BackLink from '$lib/components/ui/BackLink.svelte';
	import SourceForm from '$lib/components/SourceForm.svelte';
	import TemplatePicker from '$lib/components/TemplatePicker.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	// Templates first: writing a feed path or a set of selectors by hand is the steepest part of
	// setting a source up, and for anything common it has already been worked out.
	let byHand = $state(false);
</script>

<Page width="medium">
	<BackLink href="/sources" label="Sources" />
	<PageHeader title="New source" class="mt-3" />
	<div class="mt-5">
		{#if byHand}
			<SourceForm mode="create" playlists={data.playlists} preselectedPlaylistId={data.preselectedPlaylistId} />
		{:else}
			<TemplatePicker
				onskip={() => (byHand = true)}
				initial={data.templates}
				playlists={data.playlists}
				preselectedPlaylistId={data.preselectedPlaylistId}
			/>
		{/if}
	</div>
</Page>
