<svelte:head><title>New template - linkbelli</title></svelte:head>

<script lang="ts">
	import { goto } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { Popover } from 'bits-ui';
	import { Plus, Trash2, Save, Lock, EyeOff, Globe, X } from '@lucide/svelte';
	import CronScheduleEditor from '$lib/components/CronScheduleEditor.svelte';
	import RssSourceConfig from '$lib/components/RssSourceConfig.svelte';
	import ScraperSourceConfig from '$lib/components/ScraperSourceConfig.svelte';
	import JsonApiSourceConfig from '$lib/components/JsonApiSourceConfig.svelte';
	import type { SourceType, SourceTemplateVisibility } from '$lib/types';

	type FieldForm = {
		key: string; label: string; description: string;
		type: string; required: boolean; isSecret: boolean;
	};

	type VisOption = { label: string; icon: typeof Lock };
	const visConfig: Record<SourceTemplateVisibility, VisOption> = {
		Private: { label: 'Private', icon: Lock },
		Unlisted: { label: 'Unlisted', icon: EyeOff },
		Public: { label: 'Public', icon: Globe }
	};

	let name = $state('');
	let description = $state('');
	let type = $state<SourceType>('Rss');
	let baseConfig = $state<Record<string, string>>({});
	let fields = $state<FieldForm[]>([]);
	let visibility = $state<SourceTemplateVisibility>('Private');
	let visOpen = $state(false);
	let tags = $state<string[]>([]);
	let tagInput = $state('');
	let scheduleEnabled = $state(false);
	let schedule = $state('0 * * * *');
	let busy = $state(false);
	let error = $state<string | null>(null);

	const currentVis = $derived(visConfig[visibility]);
	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	function addField() {
		fields = [...fields, { key: '', label: '', description: '', type: 'text', required: true, isSecret: false }];
	}

	function removeField(i: number) {
		fields = fields.filter((_, j) => j !== i);
	}

	function addTag(raw: string) {
		const t = raw.trim().toLowerCase();
		if (t && !tags.includes(t)) tags = [...tags, t];
		tagInput = '';
	}

	function onTagKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter' || e.key === ',') {
			e.preventDefault();
			addTag(tagInput);
		}
	}

	async function save() {
		busy = true;
		error = null;
		try {
			const res = await api.post('/templates', {
				name,
				description: description || null,
				type,
				baseConfig,
				userFields: fields.map(f => ({ ...f, description: f.description || null })),
				defaultSchedule: scheduleEnabled ? schedule : null,
				visibility,
				tags
			});
			if (!res.ok) {
				error = 'Could not create template.';
				return;
			}
			await goto('/sources/new');
		} finally {
			busy = false;
		}
	}
</script>

<section class="mx-auto max-w-3xl">
	<a href="/sources/new" class="text-sm" style="color: var(--color-muted)">← New source</a>
	<div class="mt-3 flex items-center justify-between gap-4">
		<h1 class="text-2xl font-semibold">New template</h1>
		<!-- Visibility picker -->
		<Popover.Root bind:open={visOpen}>
			<Popover.Trigger
				class="inline-flex items-center gap-1.5 rounded-md border px-3 py-2 text-sm hover:border-[var(--color-accent)]"
				style="border-color: var(--color-border)"
				title="Change visibility"
				aria-label="Visibility"
			>
				<currentVis.icon size={14} aria-hidden="true" />
				{currentVis.label}
			</Popover.Trigger>
			<Popover.Content class="popover-surface z-30 overflow-hidden rounded-md border shadow-md" sideOffset={4} align="end">
				{#each Object.entries(visConfig) as [val, { label, icon: Icon }] (val)}
					<button
						type="button"
						onclick={() => { visibility = val as SourceTemplateVisibility; visOpen = false; }}
						class="flex w-full items-center gap-2 px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={visibility === val}
					>
						<Icon size={14} aria-hidden="true" style="color: var(--color-muted)" />
						{label}
					</button>
				{/each}
			</Popover.Content>
		</Popover.Root>
	</div>

	<div class="mt-6 flex flex-col gap-5">
		<!-- Name + type row -->
		<div class="grid grid-cols-2 gap-4">
			<label class="flex flex-col gap-1 text-sm">
				<span>Name</span>
				<input bind:value={name} class={fieldClass} style={fieldStyle} />
			</label>
			<label class="flex flex-col gap-1 text-sm">
				<span>Type</span>
				<select bind:value={type} class={fieldClass} style={fieldStyle}>
					<option value="Rss">RSS / Atom</option>
					<option value="Scraper">Web scraper</option>
					<option value="JsonApi">JSON API</option>
				</select>
			</label>
		</div>

		<label class="flex flex-col gap-1 text-sm">
			<span>Description <span style="color: var(--color-muted)">(optional)</span></span>
			<input bind:value={description} class={fieldClass} style={fieldStyle} />
		</label>

		<!-- Tags chip input -->
		<div class="flex flex-col gap-1 text-sm">
			<span>Tags</span>
			<div class="flex flex-wrap gap-1.5 rounded-md border px-2 py-2" style="border-color: var(--color-border); background: var(--color-bg)">
				{#each tags as tag (tag)}
					<span class="inline-flex items-center gap-0.5 rounded-full border px-2 py-0.5 text-xs" style="border-color: var(--color-border); background: var(--color-surface)">
						{tag}
						<button type="button" onclick={() => (tags = tags.filter(t => t !== tag))} class="ml-0.5 rounded hover:opacity-70" title="Remove tag" aria-label="Remove tag {tag}">
							<X size={10} aria-hidden="true" />
						</button>
					</span>
				{/each}
				<input
					bind:value={tagInput}
					onkeydown={onTagKeydown}
					onblur={() => tagInput.trim() && addTag(tagInput)}
					placeholder="Add tag…"
					class="min-w-[80px] flex-1 bg-transparent text-sm outline-none"
				/>
			</div>
		</div>

		<!-- Base config (type-specific, isTemplate mode) -->
		<fieldset class="rounded-lg border p-4" style="border-color: var(--color-border)">
			<legend class="px-1 text-xs" style="color: var(--color-muted)">Base config (use &#123;&#123;key&#125;&#125; for user fields)</legend>
			{#if type === 'Rss'}
				<RssSourceConfig bind:config={baseConfig} isTemplate />
			{:else if type === 'Scraper'}
				<ScraperSourceConfig bind:config={baseConfig} isTemplate />
			{:else}
				<JsonApiSourceConfig bind:config={baseConfig} isTemplate />
			{/if}
		</fieldset>

		<!-- User fields -->
		<div class="flex flex-col gap-2">
			<div class="flex items-center justify-between">
				<span class="text-sm font-medium">User fields</span>
				<button type="button" onclick={addField} class="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-accent)" title="Add field" aria-label="Add field">
					<Plus size={13} aria-hidden="true" /> Add field
				</button>
			</div>
			{#if fields.length === 0}
				<p class="text-sm" style="color: var(--color-muted)">No user fields yet. Add fields that users fill in when creating a source from this template.</p>
			{/if}
			{#each fields as field, i (i)}
				<div class="flex flex-col gap-3 rounded-lg border p-4" style="border-color: var(--color-border)">
					<div class="flex items-center justify-between">
						<span class="text-xs font-medium" style="color: var(--color-muted)">Field {i + 1}</span>
						<button type="button" onclick={() => removeField(i)} class="inline-flex items-center rounded p-0.5 hover:opacity-70" style="color: var(--color-danger)" title="Remove field" aria-label="Remove field">
							<Trash2 size={14} aria-hidden="true" />
						</button>
					</div>
					<div class="grid grid-cols-2 gap-3">
						<label class="flex flex-col gap-1 text-xs">
							<span>Key</span>
							<input bind:value={field.key} class="rounded border px-2 py-1.5 font-mono text-xs" style="border-color: var(--color-border); background: var(--color-bg)" placeholder="feedUrl" />
						</label>
						<label class="flex flex-col gap-1 text-xs">
							<span>Label</span>
							<input bind:value={field.label} class="rounded border px-2 py-1.5 text-xs" style="border-color: var(--color-border); background: var(--color-bg)" placeholder="Feed URL" />
						</label>
					</div>
					<label class="flex flex-col gap-1 text-xs">
						<span>Description <span style="color: var(--color-muted)">(optional)</span></span>
						<input bind:value={field.description} class="rounded border px-2 py-1.5 text-xs" style="border-color: var(--color-border); background: var(--color-bg)" />
					</label>
					<div class="flex gap-4 text-xs">
						<label class="flex items-center gap-1.5">
							<select bind:value={field.type} class="rounded border px-1 py-1 text-xs" style="border-color: var(--color-border); background: var(--color-bg)">
								<option value="text">text</option>
								<option value="url">url</option>
							</select>
							Type
						</label>
						<label class="flex items-center gap-1.5">
							<input type="checkbox" bind:checked={field.required} />
							Required
						</label>
						<label class="flex items-center gap-1.5">
							<input type="checkbox" bind:checked={field.isSecret} />
							Secret
						</label>
					</div>
				</div>
			{/each}
		</div>

		<!-- Default schedule -->
		<div class="flex flex-col gap-2">
			<div class="flex items-center gap-2 text-sm">
				<span class="font-medium">Default schedule</span>
				<label class="flex items-center gap-1.5 text-xs" style="color: var(--color-muted)">
					<input type="checkbox" bind:checked={scheduleEnabled} />
					Enable default
				</label>
			</div>
			{#if scheduleEnabled}
				<CronScheduleEditor bind:schedule bind:enabled={scheduleEnabled} />
			{/if}
		</div>

		{#if error}
			<p class="text-sm" style="color: var(--color-danger)">{error}</p>
		{/if}

		<div class="flex gap-3">
			<button
				type="button"
				onclick={save}
				disabled={busy}
				class="inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
				style="background: var(--color-accent); color: var(--color-accent-contrast)"
			>
				<Save size={16} aria-hidden="true" />
				Create
			</button>
			<a
				href="/sources/new"
				class="inline-flex items-center rounded-md border px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				Cancel
			</a>
		</div>
	</div>
</section>
