<script lang="ts">
	import BackLink from '$lib/components/ui/BackLink.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { api, json } from '$lib/api/client';
	import { goto } from '$app/navigation';
	import { Sparkles, Check } from '@lucide/svelte';
	import type { SourceTemplate } from '$lib/types';

	let { onskip }: { onskip: () => void } = $props();

	let templates = $state<SourceTemplate[]>([]);
	let chosen = $state<SourceTemplate | null>(null);
	let values = $state<Record<string, string>>({});
	let name = $state('');
	let busy = $state(false);
	let error = $state<string | null>(null);
	let loading = $state(true);

	// Nothing to choose from — none seeded, or the list could not be fetched — means the form
	// by hand, decided here rather than in the markup: calling back into the parent from a
	// template expression mutates its state mid-render, which Svelte refuses outright and which
	// left the page stuck on "Loading templates…" with no way to create a source at all.
	$effect(() => {
		api
			.get('/sources/templates')
			.then((res) => json<SourceTemplate[]>(res))
			.catch(() => [] as SourceTemplate[])
			.then((list) => {
				templates = list;
				loading = false;
				if (list.length === 0) onskip();
			});
	});

	function choose(template: SourceTemplate) {
		chosen = template;
		values = Object.fromEntries(template.fields.map((f) => [f.key, '']));
		// A sensible default name so the field isn't another thing to think about.
		name = template.name;
		error = null;
	}

	const ready = $derived(
		!!chosen &&
			!!name.trim() &&
			chosen.fields.every((f) => !f.required || values[f.key]?.trim())
	);

	async function create() {
		if (!chosen || !ready) return;
		busy = true;
		error = null;

		const res = await api.post('/sources', {
			name: name.trim(),
			templateId: chosen.id,
			variables: values,
			timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone
		});

		busy = false;
		if (res.ok) {
			const created = (await res.json()) as { id: string };
			goto(`/sources/${created.id}`);
		} else if (res.status === 400) {
			const problem = (await res.json().catch(() => null)) as { detail?: string } | null;
			error = problem?.detail ?? 'Check the values above.';
		} else {
			error = 'Could not create that source.';
		}
	}
</script>

{#if loading}
	<p class="text-sm" style="color: var(--color-muted)">Loading templates…</p>
{:else if templates.length === 0}
	<!-- The effect above has already handed over to the form by hand. -->
{:else if !chosen}
	<div>
		<h2 class="flex items-center gap-1.5 font-medium">
			<Sparkles size={16} aria-hidden="true" /> Start from a template
		</h2>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			The feed paths and selectors are already worked out — you just say which channel, repo or
			subreddit.
		</p>

		<div class="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
			{#each templates as template (template.id)}
				<button
					type="button"
					onclick={() => choose(template)}
					class="flex flex-col rounded-lg border p-3 text-left transition-colors hover:border-[var(--color-accent)]"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<span class="font-medium">{template.name}</span>
					<span class="mt-1 text-sm" style="color: var(--color-muted)">{template.description}</span>
				</button>
			{/each}
		</div>

		<button
			type="button"
			onclick={onskip}
			class="mt-4 text-sm underline underline-offset-2"
			style="color: var(--color-muted)"
		>Or set one up by hand</button>
	</div>
{:else}
	<div class="flex flex-col gap-3">
		<div>
			<BackLink onclick={() => (chosen = null)} label="All templates" />
			<h2 class="mt-2 font-medium">{chosen.name}</h2>
			<p class="text-sm" style="color: var(--color-muted)">{chosen.description}</p>
		</div>

		<Field label="Name">
			{#snippet children(f)}
				<Input
					id={f.id}
					bind:value={name}
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		{#each chosen.fields as field (field.key)}
			<label class="flex flex-col gap-1 text-sm">
				{field.label}{#if !field.required}<span style="color: var(--color-muted)"> (optional)</span>{/if}
				<Input
					bind:value={values[field.key]}
					placeholder={field.placeholder ?? ''}
				/>
				{#if field.help}
					<span class="text-xs" style="color: var(--color-muted)">{field.help}</span>
				{/if}
			</label>
		{/each}

		{#if error}
			<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
		{/if}

		<div class="flex items-center gap-2">
			<Button variant="primary" icon={Check} onclick={create} loading={busy} disabled={!ready}>
				{busy ? 'Creating…' : 'Create source'}
			</Button>
			<Button variant="ghost" onclick={onskip}>Set one up by hand instead</Button>
		</div>
	</div>
{/if}
