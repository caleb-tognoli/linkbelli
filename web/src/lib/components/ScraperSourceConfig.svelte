<script lang="ts">
	import { Info, Plus, X } from '@lucide/svelte';

	let { config = $bindable(), isTemplate = false }: { config: Record<string, string>; isTemplate?: boolean } = $props();

	const HEADER_PREFIX = 'header.';
	const META_FIELD_NAMES = ['title', 'thumbnail', 'author'] as const;

	let headers = $state<{ name: string; value: string }[]>(initHeaders());
	let authMode = $state<'none' | 'loginUrl'>(config['auth.loginUrl'] ? 'loginUrl' : 'none');

	function initHeaders() {
		return Object.entries(config)
			.filter(([k]) => k.startsWith(HEADER_PREFIX))
			.map(([k, v]) => ({ name: k.slice(HEADER_PREFIX.length), value: v }));
	}

	$effect(() => {
		// Sync headers back into config
		for (const k of Object.keys(config).filter(k => k.startsWith(HEADER_PREFIX))) {
			delete config[k];
		}
		for (const h of headers) {
			if (h.name.trim()) config[`${HEADER_PREFIX}${h.name.trim()}`] = h.value;
		}
	});

	$effect(() => {
		if (authMode === 'none') {
			delete config['auth.loginUrl'];
			delete config['auth.username'];
			delete config['auth.password'];
		}
	});

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';
</script>

{#snippet infoTip(text: string)}
	<span class="group relative inline-flex cursor-default">
		<Info size={12} aria-hidden="true" style="color: var(--color-muted)" />
		<span class="pointer-events-none absolute bottom-full left-1/2 z-10 mb-1.5 -translate-x-1/2 whitespace-nowrap rounded border px-2 py-1 text-xs opacity-0 shadow-sm transition-opacity group-hover:opacity-100" style="border-color: var(--color-border); background: var(--color-surface); color: var(--color-muted)">{text}</span>
	</span>
{/snippet}

<div class="flex flex-col gap-4">
	<label class="flex flex-col gap-1 text-sm">
		<span>Page URL <span style="color: var(--color-danger)">*</span></span>
		<input bind:value={config['url']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{url}}' : 'https://example.com/posts'} />
	</label>
	<label class="flex flex-col gap-1 text-sm">
		<span>Item selector <span style="color: var(--color-danger)">*</span></span>
		<input bind:value={config['itemSelector']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{itemSelector}}' : '.post-item'} />
	</label>
	<div class="flex gap-2">
		<label class="flex flex-1 flex-col gap-1 text-sm">
			<span class="inline-flex items-center gap-1">Link selector {@render infoTip('Selector is relative to the item selector')}</span>
			<input bind:value={config['linkSelector']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{linkSelector}}' : 'a.title'} />
		</label>
		<label class="flex flex-1 flex-col gap-1 text-sm">
			<span class="inline-flex items-center gap-1">Link attribute {@render infoTip('Leave blank to read text content')}</span>
			<input bind:value={config['linkAttribute']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{linkAttribute}}' : 'href'} />
		</label>
	</div>

	<!-- Request headers -->
	<div class="flex flex-col gap-2">
		<div class="flex items-center justify-between">
			<span class="text-sm">Request headers</span>
			<button type="button" onclick={() => (headers = [...headers, { name: '', value: '' }])} class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-accent)" title="Add request header" aria-label="Add request header">
				<Plus size={15} aria-hidden="true" />
			</button>
		</div>
		{#each headers as header, i (i)}
			<div class="flex gap-2">
				<input bind:value={header.name} placeholder="Name" class="{fieldClass} flex-1" style={fieldStyle} />
				<input bind:value={header.value} placeholder="Value" class="{fieldClass} flex-1" style={fieldStyle} />
				<button type="button" onclick={() => (headers = headers.filter((_, j) => j !== i))} class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-danger)" title="Remove header" aria-label="Remove header">
					<X size={17} aria-hidden="true" />
				</button>
			</div>
		{/each}
	</div>

	<!-- Authentication -->
	<div class="flex flex-col gap-3 rounded-lg border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
		<div class="flex items-center justify-between gap-4">
			<span class="text-sm font-medium">Authentication</span>
			<div class="inline-flex overflow-hidden rounded-md border text-sm" style="border-color: var(--color-border)">
				{#each [{ value: 'none', label: 'None' }, { value: 'loginUrl', label: 'Login URL' }] as opt (opt.value)}
					<button type="button" onclick={() => (authMode = opt.value as 'none' | 'loginUrl')} class="px-3 py-1.5" style={authMode === opt.value ? 'background: var(--color-accent); color: var(--color-accent-contrast)' : 'background: var(--color-bg)'} aria-pressed={authMode === opt.value}>{opt.label}</button>
				{/each}
			</div>
		</div>
		{#if authMode === 'loginUrl'}
			<label class="flex flex-col gap-1 text-sm">
				<span>Login URL</span>
				<input bind:value={config['auth.loginUrl']} class={fieldClass} style={fieldStyle} />
			</label>
			<div class="flex gap-2">
				<label class="flex flex-1 flex-col gap-1 text-sm">
					<span>Username</span>
					<input bind:value={config['auth.username']} class={fieldClass} style={fieldStyle} />
				</label>
				<label class="flex flex-1 flex-col gap-1 text-sm">
					<span>Password</span>
					<input bind:value={config['auth.password']} type="password" class={fieldClass} style={fieldStyle} />
				</label>
			</div>
		{/if}
	</div>

	<!-- Metadata -->
	<div class="flex flex-col gap-3 rounded-lg border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
		<span class="text-sm font-medium">Metadata</span>
		<div class="grid grid-cols-[7rem_1fr_1fr] items-center gap-x-2 gap-y-2 text-sm">
			<span class="text-xs font-medium" style="color: var(--color-muted)">Field</span>
			<span class="inline-flex items-center gap-1 text-xs font-medium" style="color: var(--color-muted)">Selector {@render infoTip('Selector is relative to the item selector')}</span>
			<span class="inline-flex items-center gap-1 text-xs font-medium" style="color: var(--color-muted)">Attribute {@render infoTip('Leave blank to read text content')}</span>
			{#each META_FIELD_NAMES as name (name)}
				<span class="capitalize" style="color: var(--color-muted)">{name}</span>
				<input bind:value={config[`meta.${name}`]} class={fieldClass} style={fieldStyle} />
				<input bind:value={config[`meta.${name}.attr`]} class={fieldClass} style={fieldStyle} />
			{/each}
		</div>
	</div>
</div>
