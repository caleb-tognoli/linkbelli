<script lang="ts">
	import SourceForm from '$lib/components/SourceForm.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<section class="mx-auto max-w-2xl">
	<a href="/#sources" class="text-sm" style="color: var(--color-muted)">← Sources</a>
	<h1 class="mt-3 text-2xl font-semibold">New source</h1>
	<div class="mt-5">
		<SourceForm mode="create" playlists={data.playlists} />
	</div>
</section>
