<script lang="ts">
	import { Plus, X } from '@lucide/svelte';

	let { config = $bindable(), isTemplate = false }: { config: Record<string, string>; isTemplate?: boolean } = $props();

	const HEADER_PREFIX = 'header.';

	let headers = $state<{ name: string; value: string }[]>(initHeaders());
	let authMode = $state<'none' | 'loginUrl'>(config['auth.loginUrl'] ? 'loginUrl' : 'none');

	function initHeaders() {
		return Object.entries(config)
			.filter(([k]) => k.startsWith(HEADER_PREFIX))
			.map(([k, v]) => ({ name: k.slice(HEADER_PREFIX.length), value: v }));
	}

	$effect(() => {
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

<div class="flex flex-col gap-4">
	<label class="flex flex-col gap-1 text-sm">
		<span>API URL <span style="color: var(--color-danger)">*</span></span>
		<input bind:value={config['url']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{url}}' : 'https://api.example.com/items'} />
	</label>
	<label class="flex flex-col gap-1 text-sm">
		<span>Items JSONPath <span style="color: var(--color-danger)">*</span></span>
		<input bind:value={config['itemsPath']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{itemsPath}}' : '$.items'} />
	</label>
	<div class="flex gap-2">
		<label class="flex flex-1 flex-col gap-1 text-sm">
			<span>URL JSONPath <span style="color: var(--color-danger)">*</span></span>
			<input bind:value={config['urlPath']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{urlPath}}' : '$.url'} />
		</label>
		<label class="flex flex-1 flex-col gap-1 text-sm">
			<span>Title JSONPath <span style="color: var(--color-muted)"> (optional)</span></span>
			<input bind:value={config['titlePath']} class={fieldClass} style={fieldStyle} placeholder={isTemplate ? '{{titlePath}}' : '$.title'} />
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
</div>
