<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { Popover } from 'bits-ui';
	import { Save, Lock, Globe, Trash2 } from '@lucide/svelte';
	import CronScheduleEditor from './CronScheduleEditor.svelte';
	import type { Source, SourceTemplate, SourceVisibility } from '$lib/types';

	const CRON_NEVER = '0 0 30 2 *';

	type VisOption = { label: string; icon: typeof Lock };
	const visConfig: Record<SourceVisibility, VisOption> = {
		Private: { label: 'Private', icon: Lock },
		Shared: { label: 'Shared', icon: Globe }
	};

	let {
		mode,
		template,
		source,
		ondelete
	}: {
		mode: 'create' | 'edit';
		template: SourceTemplate;
		source?: Source;
		ondelete?: () => void;
	} = $props();

	const initSched = source?.schedule ?? template.defaultSchedule ?? '0 * * * *';

	let name = $state(source?.name ?? '');
	let visibility = $state<SourceVisibility>(source?.visibility ?? 'Private');
	let schedule = $state(initSched);
	let enabled = $state(initSched !== CRON_NEVER);

	let params = $state<Record<string, string>>(
		Object.fromEntries(template.userFields.map(f => [f.key, source?.config?.[f.key] ?? '']))
	);

	let busy = $state(false);
	let error = $state<string | null>(null);
	let visOpen = $state(false);
	const currentVis = $derived(visConfig[visibility] ?? visConfig.Private);

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';

	async function save() {
		busy = true;
		error = null;
		try {
			let res: Response;
			if (mode === 'create') {
				res = await api.post('/sources/from-template', {
					templateId: template.id,
					name,
					userParams: params,
					schedule,
					visibility
				});
			} else {
				res = await api.patch(`/sources/${source!.id}`, {
					name,
					config: params,
					schedule,
					visibility
				});
			}
			if (!res.ok) {
				error = res.status === 429
					? 'You have reached your source quota.'
					: 'Could not save — check the fields.';
				return;
			}
			if (mode === 'create') {
				const created = (await res.json()) as Source;
				await goto(`/sources/${created.id}`);
			} else {
				await invalidateAll();
			}
		} finally {
			busy = false;
		}
	}
</script>

<div class="flex flex-col gap-4">
	<!-- Name + visibility -->
	<div class="flex flex-col gap-1 text-sm">
		<span>Name</span>
		<div class="flex items-center gap-2">
			<input bind:value={name} class="{fieldClass} flex-1" style={fieldStyle} />
			<Popover.Root bind:open={visOpen}>
				<Popover.Trigger
					class="inline-flex shrink-0 items-center gap-1.5 rounded-md border px-3 py-2 text-sm hover:border-[var(--color-accent)]"
					style="border-color: var(--color-border)"
					title="Change visibility"
					aria-label="Visibility"
				>
					<currentVis.icon size={14} aria-hidden="true" />
					{currentVis.label}
				</Popover.Trigger>
				<Popover.Content
					class="popover-surface z-30 overflow-hidden rounded-md border shadow-md"
					sideOffset={4}
					align="end"
				>
					{#each Object.entries(visConfig) as [val, { label, icon: Icon }] (val)}
						<button
							type="button"
							onclick={() => { visibility = val as SourceVisibility; visOpen = false; }}
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
	</div>

	<CronScheduleEditor bind:schedule bind:enabled />

	<!-- Template user fields -->
	<fieldset class="rounded-lg border p-4" style="border-color: var(--color-border)">
		<legend class="px-1 text-xs" style="color: var(--color-muted)">Configuration</legend>
		<div class="flex flex-col gap-4">
			{#each template.userFields as field (field.key)}
				<label class="flex flex-col gap-1 text-sm">
					<span>
						{field.label}
						{#if !field.required}<span style="color: var(--color-muted)"> (optional)</span>{/if}
					</span>
					{#if field.description}
						<p class="text-xs" style="color: var(--color-muted)">{field.description}</p>
					{/if}
					<input
						bind:value={params[field.key]}
						type={field.isSecret ? 'password' : field.type === 'url' ? 'url' : 'text'}
						class={fieldClass}
						style={fieldStyle}
					/>
				</label>
			{/each}
			{#if template.userFields.length === 0}
				<p class="text-sm" style="color: var(--color-muted)">This template has no configurable fields.</p>
			{/if}
		</div>
	</fieldset>

	<div class="flex items-center gap-3">
		<button
			type="button"
			onclick={save}
			disabled={busy}
			class="inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
			style="background: var(--color-accent); color: var(--color-accent-contrast)"
		>
			<Save size={16} aria-hidden="true" />
			{mode === 'create' ? 'Create' : 'Save'}
		</button>
		{#if ondelete}
			<button
				type="button"
				onclick={ondelete}
				disabled={busy}
				class="inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
				style="color: var(--color-danger)"
			>
				<Trash2 size={16} aria-hidden="true" />
				Delete
			</button>
		{/if}
		{#if error}
			<p class="text-sm" style="color: var(--color-danger)">{error}</p>
		{/if}
	</div>
</div>
