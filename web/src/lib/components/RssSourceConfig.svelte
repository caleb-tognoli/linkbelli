<script lang="ts">
	let { config = $bindable(), isTemplate = false }: { config: Record<string, string>; isTemplate?: boolean } = $props();

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';
</script>

<div class="flex flex-col gap-4">
	<label class="flex flex-col gap-1 text-sm">
		<span>Feed URL <span style="color: var(--color-danger)">*</span></span>
		<input
			bind:value={config['feedUrl']}
			class={fieldClass}
			style={fieldStyle}
			placeholder={isTemplate ? 'e.g. {{feedUrl}}' : 'https://example.com/feed.xml'}
		/>
	</label>
</div>
