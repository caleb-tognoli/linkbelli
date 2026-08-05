<svelte:head><title>New source - linkbelli</title></svelte:head>

<script lang="ts">
	import { api } from '$lib/api/client';
	import SourceForm from '$lib/components/SourceForm.svelte';
	import TemplateGallery from '$lib/components/TemplateGallery.svelte';
	import TemplateSourceForm from '$lib/components/TemplateSourceForm.svelte';
	import type { SourceTemplate, SourceType } from '$lib/types';

	let templates = $state<SourceTemplate[]>([]);
	let loadingTemplates = $state(true);
	let step = $state<'gallery' | 'template' | 'rss' | 'scraper' | 'jsonapi'>('gallery');
	let selectedTemplate = $state<SourceTemplate | null>(null);

	$effect(() => {
		api.get('/templates').then(async (res) => {
			if (res.ok) templates = await res.json();
		}).finally(() => {
			loadingTemplates = false;
		});
	});

	function onselect(template: SourceTemplate) {
		selectedTemplate = template;
		step = 'template';
	}

	function onadvanced(type: SourceType) {
		step = type.toLowerCase() as 'rss' | 'scraper' | 'jsonapi';
	}

	function back() {
		step = 'gallery';
		selectedTemplate = null;
	}

	const lockedTypeMap: Record<'rss' | 'scraper' | 'jsonapi', SourceType> = {
		rss: 'Rss',
		scraper: 'Scraper',
		jsonapi: 'JsonApi'
	};
</script>

<section class="mx-auto max-w-4xl">
	{#if step === 'gallery'}
		<a href="/#sources" class="text-sm" style="color: var(--color-muted)">← Sources</a>
		<h1 class="mt-3 text-2xl font-semibold">New source</h1>
		<div class="mt-5">
			{#if loadingTemplates}
				<p class="text-sm" style="color: var(--color-muted)">Loading…</p>
			{:else}
				<TemplateGallery {templates} {onselect} {onadvanced} />
			{/if}
		</div>
	{:else if step === 'template' && selectedTemplate}
		<button type="button" onclick={back} class="text-sm" style="color: var(--color-muted)">← Back</button>
		<h1 class="mt-3 text-2xl font-semibold">{selectedTemplate.name}</h1>
		{#if selectedTemplate.description}
			<p class="mt-1 text-sm" style="color: var(--color-muted)">{selectedTemplate.description}</p>
		{/if}
		<div class="mt-5">
			<TemplateSourceForm mode="create" template={selectedTemplate} />
		</div>
	{:else if step === 'rss' || step === 'scraper' || step === 'jsonapi'}
		<button type="button" onclick={back} class="text-sm" style="color: var(--color-muted)">← Back</button>
		<h1 class="mt-3 text-2xl font-semibold">New source</h1>
		<div class="mt-5">
			<SourceForm mode="create" lockedType={lockedTypeMap[step]} />
		</div>
	{/if}
</section>
