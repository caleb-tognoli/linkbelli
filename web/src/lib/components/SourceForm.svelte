<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import type { Playlist, PreviewResult, Source, SourceType, SourceVisibility } from '$lib/types';

	let {
		mode,
		source,
		playlists
	}: { mode: 'create' | 'edit'; source?: Source; playlists: Playlist[] } = $props();

	interface FieldDef {
		key: string;
		label: string;
		required: boolean;
		placeholder?: string;
	}

	const FIELDS: Record<SourceType, FieldDef[]> = {
		Rss: [{ key: 'feedUrl', label: 'Feed URL', required: true, placeholder: 'https://…/feed.xml' }],
		Scraper: [
			{ key: 'url', label: 'Page URL', required: true, placeholder: 'https://news.example' },
			{ key: 'itemSelector', label: 'Item selector (CSS)', required: true, placeholder: 'a.headline' },
			{ key: 'linkAttribute', label: 'Link attribute', required: false, placeholder: 'href' },
			{ key: 'titleSelector', label: 'Title selector (CSS)', required: false }
		],
		JsonApi: [
			{ key: 'url', label: 'API URL', required: true, placeholder: 'https://api.example/posts' },
			{ key: 'itemsPath', label: 'Items JSONPath', required: true, placeholder: '$.data.posts[*]' },
			{ key: 'urlPath', label: 'URL JSONPath', required: true, placeholder: 'permalink' },
			{ key: 'titlePath', label: 'Title JSONPath', required: false, placeholder: 'title' }
		]
	};

	const HEADER_PREFIX = 'header.';

	let name = $state(source?.name ?? '');
	let type = $state<SourceType>(source?.type ?? 'Rss');
	let visibility = $state<SourceVisibility>(source?.visibility ?? 'Private');
	let schedule = $state(source?.schedule ?? '0 * * * *');
	let enabled = $state(source?.enabled ?? true);

	// Config field values (non-header) for the current type.
	let values = $state<Record<string, string>>(initValues());
	let headers = $state<{ name: string; value: string }[]>(initHeaders());
	let selected = $state<Set<string>>(
		new Set((source?.playlistIds ?? []).filter((id) => playlists.some((p) => p.id === id)))
	);

	let preview = $state<PreviewResult | null>(null);
	let busy = $state(false);
	let error = $state<string | null>(null);

	function initValues(): Record<string, string> {
		const v: Record<string, string> = {};
		if (source) {
			for (const [k, val] of Object.entries(source.config)) {
				if (!k.startsWith(HEADER_PREFIX)) v[k] = val;
			}
		}
		return v;
	}

	function initHeaders(): { name: string; value: string }[] {
		if (!source) return [];
		return Object.entries(source.config)
			.filter(([k]) => k.startsWith(HEADER_PREFIX))
			.map(([k, val]) => ({ name: k.slice(HEADER_PREFIX.length), value: val }));
	}

	function buildConfig(): Record<string, string> {
		const cfg: Record<string, string> = {};
		for (const f of FIELDS[type]) {
			const v = values[f.key]?.trim();
			if (v) cfg[f.key] = v;
		}
		if (type === 'JsonApi') {
			for (const h of headers) {
				if (h.name.trim() && h.value) cfg[`${HEADER_PREFIX}${h.name.trim()}`] = h.value;
			}
		}
		return cfg;
	}

	async function doPreview() {
		busy = true;
		error = null;
		preview = null;
		try {
			const res = await api.post('/sources/preview', { type, config: buildConfig() });
			if (!res.ok) {
				error = 'Preview failed — check the URL and config.';
				return;
			}
			preview = (await res.json()) as PreviewResult;
		} finally {
			busy = false;
		}
	}

	async function save() {
		// Warn before dropping other users' subscriptions.
		if (mode === 'edit' && source!.visibility === 'Shared' && visibility === 'Private') {
			const ok = confirm(
				"Switching this source to Private will unsubscribe it from other users' playlists that follow it. Continue?"
			);
			if (!ok) return;
		}

		busy = true;
		error = null;
		try {
			const config = buildConfig();
			const playlistIds = [...selected];
			let res: Response;
			if (mode === 'create') {
				res = await api.post('/sources', { name, type, config, schedule, playlistIds, visibility });
			} else {
				res = await api.patch(`/sources/${source!.id}`, { name, schedule, enabled, config, playlistIds, visibility });
			}
			if (!res.ok) {
				error =
					res.status === 429
						? 'You have reached your source quota.'
						: 'Could not save — check the name, schedule (min 5-minute cron) and config.';
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

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';
</script>

<div class="flex flex-col gap-4">
	<label class="flex flex-col gap-1 text-sm">
		<span>Name</span>
		<input bind:value={name} class={fieldClass} style={fieldStyle} />
	</label>

	<div class="flex flex-wrap gap-4">
		<label class="flex flex-col gap-1 text-sm">
			<span>Type</span>
			{#if mode === 'create'}
				<select bind:value={type} class={fieldClass} style={fieldStyle}>
					<option value="Rss">RSS / Atom</option>
					<option value="Scraper">Web scraper</option>
					<option value="JsonApi">JSON API</option>
				</select>
			{:else}
				<span class="{fieldClass} inline-block" style={fieldStyle}>{type}</span>
			{/if}
		</label>

		<label class="flex flex-col gap-1 text-sm">
			<span>Schedule (cron)</span>
			<input bind:value={schedule} class={fieldClass} style={fieldStyle} placeholder="*/15 * * * *" />
		</label>

		<label class="flex flex-col gap-1 text-sm">
			<span>Visibility</span>
			<select bind:value={visibility} class={fieldClass} style={fieldStyle}>
				<option value="Private">Private</option>
				<option value="Shared">Shared (others can subscribe)</option>
			</select>
		</label>

		{#if mode === 'edit'}
			<label class="flex items-center gap-2 self-end text-sm">
				<input type="checkbox" bind:checked={enabled} /> Enabled
			</label>
		{/if}
	</div>

	<fieldset class="flex flex-col gap-3 rounded-lg border p-3" style="border-color: var(--color-border)">
		<legend class="px-1 text-xs" style="color: var(--color-muted)">Configuration</legend>
		{#each FIELDS[type] as f (f.key)}
			<label class="flex flex-col gap-1 text-sm">
				<span>{f.label} {#if !f.required}<span style="color: var(--color-muted)">(optional)</span>{/if}</span>
				<input bind:value={values[f.key]} placeholder={f.placeholder ?? ''} class={fieldClass} style={fieldStyle} />
			</label>
		{/each}

		{#if type === 'JsonApi'}
			<div class="flex flex-col gap-2">
				<span class="text-sm">Request headers <span style="color: var(--color-muted)">(stored encrypted; shown as ***)</span></span>
				{#each headers as header, i (i)}
					<div class="flex gap-2">
						<input bind:value={header.name} placeholder="Authorization" class="{fieldClass} flex-1" style={fieldStyle} />
						<input bind:value={header.value} placeholder="Bearer …" class="{fieldClass} flex-1" style={fieldStyle} />
						<button type="button" onclick={() => (headers = headers.filter((_, j) => j !== i))} class="px-2" style="color: var(--color-danger)">✕</button>
					</div>
				{/each}
				<button type="button" onclick={() => (headers = [...headers, { name: '', value: '' }])} class="self-start text-xs" style="color: var(--color-accent)">
					+ Add header
				</button>
			</div>
		{/if}
	</fieldset>

	{#if playlists.length}
		<fieldset class="flex flex-col gap-1.5 rounded-lg border p-3" style="border-color: var(--color-border)">
			<legend class="px-1 text-xs" style="color: var(--color-muted)">Feed into your playlists</legend>
			{#each playlists as pl (pl.id)}
				<label class="flex items-center gap-2 text-sm">
					<input
						type="checkbox"
						checked={selected.has(pl.id)}
						onchange={(e) => {
							const next = new Set(selected);
							if (e.currentTarget.checked) next.add(pl.id);
							else next.delete(pl.id);
							selected = next;
						}}
					/>
					{pl.name}
				</label>
			{/each}
		</fieldset>
	{/if}

	{#if error}
		<p class="text-sm" style="color: var(--color-danger)">{error}</p>
	{/if}

	<div class="flex items-center gap-2">
		<button type="button" onclick={save} disabled={busy} class="rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60" style="background: var(--color-accent); color: var(--color-accent-contrast)">
			{mode === 'create' ? 'Create source' : 'Save changes'}
		</button>
		<button type="button" onclick={doPreview} disabled={busy} class="rounded-md border px-3 py-2 text-sm disabled:opacity-60" style="border-color: var(--color-border)">
			Preview
		</button>
	</div>

	{#if preview}
		<div class="rounded-lg border p-3 text-sm" style="border-color: var(--color-border)">
			<div class="font-medium">Preview — {preview.count} link{preview.count === 1 ? '' : 's'}</div>
			<ul class="mt-2 flex flex-col gap-1">
				{#each preview.links as link (link.url)}
					<li class="truncate" style="color: var(--color-muted)">{link.title ?? link.url}</li>
				{/each}
			</ul>
		</div>
	{/if}
</div>
