<svelte:head><title>Templates - linkbelli</title></svelte:head>

<script lang="ts">
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Plus, Pencil, Trash2, Rss, Code2, Braces } from '@lucide/svelte';
	import type { SourceTemplateDetail, SourceType, SourceTemplateVisibility } from '$lib/types';

	const typeIcons: Record<SourceType, typeof Rss> = { Rss, Scraper: Code2, JsonApi: Braces };
	const typeLabels: Record<SourceType, string> = { Rss: 'RSS', Scraper: 'Scraper', JsonApi: 'JSON API' };
	const visLabels: Record<SourceTemplateVisibility, string> = { Private: 'Private', Unlisted: 'Unlisted', Public: 'Public' };

	let templates = $state<SourceTemplateDetail[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	async function load() {
		loading = true;
		const res = await api.get('/templates/all');
		if (res.ok) templates = await res.json();
		else error = 'Could not load templates.';
		loading = false;
	}

	$effect(() => { load(); });

	async function remove(t: SourceTemplateDetail) {
		if (!(await confirmDialog(`Delete "${t.name}"? Sources using it will keep running with the last resolved config.`, { danger: true, confirmLabel: 'Delete' }))) return;
		const res = await api.del(`/templates/${t.id}`);
		if (res.ok) await load();
	}

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	// --- Inline create/edit form ---
	let editing = $state<SourceTemplateDetail | null>(null);
	let creating = $state(false);

	type FieldForm = { key: string; label: string; description: string; type: string; required: boolean; isSecret: boolean };

	function blankForm(): {
		name: string; description: string; type: SourceType;
		baseConfigRaw: string; fields: FieldForm[];
		defaultSchedule: string; visibility: SourceTemplateVisibility; isSystem: boolean;
	} {
		return {
			name: '', description: '', type: 'Scraper',
			baseConfigRaw: '{}', fields: [],
			defaultSchedule: '0 * * * *', visibility: 'Private', isSystem: false
		};
	}

	let form = $state(blankForm());
	let formError = $state<string | null>(null);
	let formBusy = $state(false);

	function openCreate() {
		editing = null;
		form = blankForm();
		formError = null;
		creating = true;
	}

	function openEdit(t: SourceTemplateDetail) {
		creating = false;
		form = {
			name: t.name,
			description: t.description ?? '',
			type: t.type,
			baseConfigRaw: JSON.stringify(t.baseConfig, null, 2),
			fields: t.userFields.map(f => ({ ...f, description: f.description ?? '' })),
			defaultSchedule: t.defaultSchedule ?? '0 * * * *',
			visibility: t.visibility,
			isSystem: t.ownerId === null
		};
		formError = null;
		editing = t;
	}

	function closeForm() {
		creating = false;
		editing = null;
	}

	function addField() {
		form.fields = [...form.fields, { key: '', label: '', description: '', type: 'text', required: true, isSecret: false }];
	}

	function removeField(i: number) {
		form.fields = form.fields.filter((_, j) => j !== i);
	}

	async function submitForm() {
		formBusy = true;
		formError = null;
		try {
			let baseConfig: Record<string, string>;
			try {
				baseConfig = JSON.parse(form.baseConfigRaw);
			} catch {
				formError = 'Base config is not valid JSON.';
				return;
			}

			const body = {
				name: form.name,
				description: form.description || null,
				type: form.type,
				baseConfig,
				userFields: form.fields,
				defaultSchedule: form.defaultSchedule || null,
				visibility: form.visibility,
				isSystem: form.isSystem
			};

			const res = editing
				? await api.patch(`/templates/${editing.id}`, body)
				: await api.post('/templates', body);

			if (!res.ok) {
				formError = 'Could not save template.';
				return;
			}
			closeForm();
			await load();
		} finally {
			formBusy = false;
		}
	}
</script>

<section class="mx-auto max-w-4xl">
	<div class="flex items-center justify-between">
		<h1 class="text-2xl font-semibold">Templates</h1>
		<button
			type="button"
			onclick={openCreate}
			class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium"
			style="background: var(--color-accent); color: var(--color-accent-contrast)"
		>
			<Plus size={16} aria-hidden="true" /> New template
		</button>
	</div>

	{#if loading}
		<p class="mt-4 text-sm" style="color: var(--color-muted)">Loading…</p>
	{:else if error}
		<p class="mt-4 text-sm" style="color: var(--color-danger)">{error}</p>
	{:else if templates.length === 0}
		<p class="mt-4 text-sm" style="color: var(--color-muted)">No templates yet.</p>
	{:else}
		<ul class="mt-4 flex flex-col gap-3">
			{#each templates as t (t.id)}
				{@const Icon = typeIcons[t.type] ?? Braces}
				<li class="flex items-center gap-4 rounded-lg border px-4 py-3" style="border-color: var(--color-border); background: var(--color-surface)">
					<Icon size={16} aria-hidden="true" style="color: var(--color-muted)" />
					<div class="min-w-0 flex-1">
						<div class="flex items-center gap-2">
							<span class="font-medium">{t.name}</span>
							<span class="text-xs" style="color: var(--color-muted)">{typeLabels[t.type]}</span>
							{#if t.ownerId === null}
								<span class="rounded px-1 text-xs font-medium" style="background: var(--color-surface); color: var(--color-accent)">System</span>
							{/if}
							<span class="text-xs" style="color: var(--color-muted)">{visLabels[t.visibility]}</span>
						</div>
						{#if t.description}
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">{t.description}</p>
						{/if}
					</div>
					<div class="flex shrink-0 items-center gap-1">
						<button
							type="button"
							onclick={() => openEdit(t)}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							title="Edit template"
							aria-label="Edit template"
						>
							<Pencil size={16} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => remove(t)}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							title="Delete template"
							aria-label="Delete template"
							style="color: var(--color-danger)"
						>
							<Trash2 size={16} aria-hidden="true" />
						</button>
					</div>
				</li>
			{/each}
		</ul>
	{/if}

	{#if creating || editing}
		<div class="mt-8 rounded-lg border p-6" style="border-color: var(--color-border); background: var(--color-surface)">
			<h2 class="mb-4 font-semibold">{editing ? 'Edit template' : 'New template'}</h2>
			<div class="flex flex-col gap-4">
				<div class="grid grid-cols-2 gap-4">
					<label class="flex flex-col gap-1 text-sm">
						<span>Name</span>
						<input bind:value={form.name} class={fieldClass} style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>Type</span>
						<select bind:value={form.type} class={fieldClass} style={fieldStyle}>
							<option value="Rss">RSS / Atom</option>
							<option value="Scraper">Web scraper</option>
							<option value="JsonApi">JSON API</option>
						</select>
					</label>
				</div>

				<label class="flex flex-col gap-1 text-sm">
					<span>Description <span style="color: var(--color-muted)">(optional)</span></span>
					<input bind:value={form.description} class={fieldClass} style={fieldStyle} />
				</label>

				<label class="flex flex-col gap-1 text-sm">
					<span>Default schedule <span style="color: var(--color-muted)">(cron)</span></span>
					<input bind:value={form.defaultSchedule} class={fieldClass} style={fieldStyle} />
				</label>

				<label class="flex flex-col gap-1 text-sm">
					<span>Base config <span style="color: var(--color-muted)">(JSON — use &#123;&#123;key&#125;&#125; for user fields)</span></span>
					<textarea bind:value={form.baseConfigRaw} rows={8} class="{fieldClass} font-mono" style={fieldStyle}></textarea>
				</label>

				<!-- User fields -->
				<div class="flex flex-col gap-2">
					<div class="flex items-center justify-between">
						<span class="text-sm font-medium">User fields</span>
						<button type="button" onclick={addField} class="inline-flex items-center rounded p-1 hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-accent)" title="Add field" aria-label="Add field">
							<Plus size={15} aria-hidden="true" />
						</button>
					</div>
					{#each form.fields as field, i (i)}
						<div class="grid grid-cols-[1fr_1fr_auto_auto_auto_auto] items-center gap-2 rounded-md border px-3 py-2" style="border-color: var(--color-border)">
							<input bind:value={field.key} placeholder="key" class="rounded border px-2 py-1 text-xs font-mono" style="border-color: var(--color-border); background: var(--color-bg)" />
							<input bind:value={field.label} placeholder="Label" class="rounded border px-2 py-1 text-xs" style="border-color: var(--color-border); background: var(--color-bg)" />
							<label class="flex items-center gap-1 text-xs">
								<input type="checkbox" bind:checked={field.required} /> Req
							</label>
							<label class="flex items-center gap-1 text-xs">
								<input type="checkbox" bind:checked={field.isSecret} /> Secret
							</label>
							<select bind:value={field.type} class="rounded border px-1 py-1 text-xs" style="border-color: var(--color-border); background: var(--color-bg)">
								<option value="text">text</option>
								<option value="url">url</option>
							</select>
							<button type="button" onclick={() => removeField(i)} class="inline-flex items-center rounded p-0.5 hover:opacity-70" style="color: var(--color-danger)" title="Remove" aria-label="Remove field">
								<Trash2 size={13} aria-hidden="true" />
							</button>
						</div>
					{/each}
				</div>

				<div class="flex items-center gap-6 text-sm">
					<label class="flex flex-col gap-1">
						<span>Visibility</span>
						<select bind:value={form.visibility} class={fieldClass} style={fieldStyle}>
							<option value="Private">Private</option>
							<option value="Unlisted">Unlisted</option>
							<option value="Public">Public</option>
						</select>
					</label>
					<label class="flex items-center gap-2">
						<input type="checkbox" bind:checked={form.isSystem} />
						System template (null OwnerId)
					</label>
				</div>

				{#if formError}
					<p class="text-sm" style="color: var(--color-danger)">{formError}</p>
				{/if}

				<div class="flex gap-3">
					<button
						type="button"
						onclick={submitForm}
						disabled={formBusy}
						class="inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
						style="background: var(--color-accent); color: var(--color-accent-contrast)"
					>
						{editing ? 'Save' : 'Create'}
					</button>
					<button
						type="button"
						onclick={closeForm}
						class="rounded-md border px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						Cancel
					</button>
				</div>
			</div>
		</div>
	{/if}
</section>
