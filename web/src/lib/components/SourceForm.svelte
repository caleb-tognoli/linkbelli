<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { Popover } from 'bits-ui';
	import { X, Plus, Save, Lock, Globe, Trash2, Info, ChevronRight } from '@lucide/svelte';
	import Switch from './Switch.svelte';
	import type { Source, SourceFilter, SourceType, SourceVisibility } from '$lib/types';
	import { isPreviewable, previewKey, type SourcePreview } from '$lib/sourcePreview';

	type VisOption = { label: string; icon: typeof Lock };
	const visConfig: Record<SourceVisibility, VisOption> = {
		Private: { label: 'Private', icon: Lock },
		Shared: { label: 'Shared', icon: Globe }
	};

	let {
		mode,
		source,
		ondelete
	}: { mode: 'create' | 'edit'; source?: Source; ondelete?: () => void } = $props();

	// Unique per form, so the name field's label can point at it even with two forms on a page.
	const uid = $props.id();

	interface FieldDef {
		key: string;
		label: string;
		required: boolean;
		placeholder?: string;
		inputType?: string;
		hideOptional?: boolean;
	}

	const AUTH_FIELDS: FieldDef[] = [
		{ key: 'auth.loginUrl', label: 'Login URL', required: false },
		{ key: 'auth.username', label: 'Username', required: false },
		{ key: 'auth.password', label: 'Password', required: false, inputType: 'password' }
	];

	const META_FIELD_NAMES = ['title', 'thumbnail', 'author'] as const;

	const SCRAPER_LINK_FIELDS = ['linkSelector', 'linkAttribute'] as const;

	const FIELDS: Record<SourceType, FieldDef[]> = {
		// A webhook has nothing to configure: it is addressed by a token the server mints.
		Webhook: [],
		Rss: [{ key: 'feedUrl', label: 'Feed URL', required: true }],
		Scraper: [
			{ key: 'url', label: 'Page URL', required: true },
			{ key: 'itemSelector', label: 'Item selector', required: true },
			{ key: 'linkSelector', label: 'Link selector (CSS)', required: false, hideOptional: true },
			{ key: 'linkAttribute', label: 'Link attribute', required: false, hideOptional: true }
		],
		JsonApi: [
			{ key: 'url', label: 'API URL', required: true },
			{ key: 'itemsPath', label: 'Items JSONPath', required: true },
			{ key: 'urlPath', label: 'URL JSONPath', required: false },
			{ key: 'urlTemplate', label: 'URL template', required: false, placeholder: 'https://example.com/movie/{id}/{slug}' },
			{ key: 'titlePath', label: 'Title JSONPath', required: false }
		]
	};

	const HEADER_PREFIX = 'header.';

	function parseCron(cron: string): { count: number; unit: 'minutes' | 'hours' | 'days' } {
		const m = cron.match(/^\*\/(\d+) \* \* \* \*$/);
		if (m) return { count: +m[1], unit: 'minutes' };
		const h = cron.match(/^0 \*\/(\d+) \* \* \*$/);
		if (h) return { count: +h[1], unit: 'hours' };
		if (cron === '0 * * * *') return { count: 1, unit: 'hours' };
		const d = cron.match(/^0 0 \*\/(\d+) \* \*$/);
		if (d) return { count: +d[1], unit: 'days' };
		if (cron === '0 0 * * *') return { count: 1, unit: 'days' };
		return { count: 1, unit: 'hours' };
	}

	function buildCron(count: number, unit: 'minutes' | 'hours' | 'days'): string {
		if (unit === 'minutes') return `*/${Math.max(5, count)} * * * *`;
		if (unit === 'hours') return count === 1 ? '0 * * * *' : `0 */${count} * * *`;
		return count === 1 ? '0 0 * * *' : `0 0 */${count} * *`;
	}

	const _sched = parseCron(source?.schedule ?? '0 * * * *');

	let name = $state(source?.name ?? '');
	let type = $state<SourceType>(source?.type ?? 'Rss');
	let visibility = $state<SourceVisibility>(source?.visibility ?? 'Private');
	let scheduleCount = $state(_sched.count);
	let scheduleUnit = $state<'minutes' | 'hours' | 'days'>(_sched.unit);
	// Pausing is a status on the source, not a cron that never fires: the schedule below stays
	// exactly as the owner set it, so resuming picks the same cadence back up.
	let enabled = $state((source?.status ?? 'Active') === 'Active');
	const schedule = $derived(buildCron(scheduleCount, scheduleUnit));
	const status = $derived(enabled ? 'Active' : 'Paused');
	// A source that stopped itself needs the owner to fix something and switch it back on; the
	// toggle starts off so saving is a deliberate act rather than an accident.
	const wasFailing = source?.status === 'Failing';

	// A new source is scheduled in the browser's own zone, so "every day at 8" means eight where
	// the person setting it lives. Existing sources keep whatever they were created with.
	const timeZone =
		source?.timeZone ?? (source ? null : Intl.DateTimeFormat().resolvedOptions().timeZone);

	// Config field values (non-header) for the current type.
	let values = $state<Record<string, string>>(initValues());
	let headers = $state<{ name: string; value: string }[]>(initHeaders());
	// auth.loginUrl is plaintext in the response, so truthy = Login URL mode.
	let authMode = $state<'none' | 'loginUrl'>(
		source?.config?.['auth.loginUrl'] ? 'loginUrl' : 'none'
	);

	// Everything a source finds lands unless one of these turns it away. Held as strings so the
	// number boxes can be genuinely empty rather than stuck at a zero that means something.
	let filter = $state({
		titleInclude: source?.filter?.titleInclude ?? '',
		titleExclude: source?.filter?.titleExclude ?? '',
		urlInclude: source?.filter?.urlInclude ?? '',
		urlExclude: source?.filter?.urlExclude ?? '',
		minAgeHours: numeric(source?.filter?.minAgeHours),
		maxItems: numeric(source?.filter?.maxItems),
		dedupeWindowDays: numeric(source?.filter?.dedupeWindowDays)
	});

	let filtersOpen = $state(hasFilter(source?.filter));

	// Built here rather than server-side: the origin someone is looking at is the one that will
	// actually reach this instance.
	const webhookUrl = $derived(
		source?.webhookToken && typeof location !== 'undefined'
			? `${location.origin}/api/v1/hooks/${source.webhookToken}`
			: null
	);

	let copied = $state(false);

	async function copyWebhookUrl() {
		if (!webhookUrl) return;
		try {
			await navigator.clipboard.writeText(webhookUrl);
			copied = true;
			setTimeout(() => (copied = false), 2000);
		} catch {
			// The URL is on screen either way.
		}
	}

	// A dry run of the config as it stands. Until now the only way to find out whether a selector
	// matched anything was to save the source, wait for its first run, and read the history.
	let preview = $state<SourcePreview | null>(null);
	let previewing = $state(false);
	let previewError = $state<string | null>(null);
	let lastPreviewed = '';

	async function runPreview(key: string, config: Record<string, string>) {
		previewing = true;
		previewError = null;
		try {
			const res = await api.post('/sources/preview', { type, config });

			if (res.ok) {
				preview = (await res.json()) as SourcePreview;
				lastPreviewed = key;
				return;
			}

			preview = null;
			previewError =
				res.status === 429
					? 'Too many previews just now — try again in a moment.'
					: ((await problem(res)) ?? 'Could not read that source.');
			// Remembered even on failure, so a broken config is not retried on every keystroke.
			lastPreviewed = key;
		} catch {
			preview = null;
			previewError = 'Could not reach that source.';
			lastPreviewed = key;
		} finally {
			previewing = false;
		}
	}

	$effect(() => {
		const config = buildConfig();
		const key = previewKey(type, config);

		if (!isPreviewable(type, config)) {
			preview = null;
			previewError = null;
			return;
		}

		if (key === lastPreviewed) return;

		// Generous, because this is a live outbound fetch on a rate-limited endpoint and the
		// person is still typing.
		const timer = setTimeout(() => runPreview(key, config), 1500);
		return () => clearTimeout(timer);
	});

	let busy = $state(false);
	let error = $state<string | null>(null);
	let visOpen = $state(false);
	const currentVis = $derived(visConfig[visibility] ?? visConfig.Private);

	function numeric(value: number | null | undefined): string {
		return value === null || value === undefined ? '' : String(value);
	}

	function hasFilter(value: SourceFilter | null | undefined): boolean {
		return !!value && Object.values(value).some((v) => v !== null && v !== undefined);
	}

	/**
	 * The filter as the API takes it. Always sent, even empty: an omitted filter means "leave the
	 * stored one alone", so clearing the last box has to say so explicitly.
	 */
	function buildFilter(): Record<string, string | number> {
		const built: Record<string, string | number> = {};
		for (const key of ['titleInclude', 'titleExclude', 'urlInclude', 'urlExclude'] as const) {
			const value = filter[key].trim();
			if (value) built[key] = value;
		}
		for (const key of ['minAgeHours', 'maxItems', 'dedupeWindowDays'] as const) {
			const value = filter[key].trim();
			if (value !== '' && Number.isFinite(+value)) built[key] = +value;
		}
		return built;
	}

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

	function setAuthMode(mode: 'none' | 'loginUrl') {
		authMode = mode;
	}

	function buildConfig(): Record<string, string> {
		const cfg: Record<string, string> = {};
		for (const f of FIELDS[type]) {
			const v = values[f.key]?.trim();
			if (v) cfg[f.key] = v;
		}
		if (type === 'JsonApi' || type === 'Scraper') {
			for (const h of headers) {
				if (h.name.trim() && h.value) cfg[`${HEADER_PREFIX}${h.name.trim()}`] = h.value;
			}
			if (authMode === 'loginUrl') {
				for (const f of AUTH_FIELDS) {
					const v = values[f.key]?.trim();
					if (v) cfg[f.key] = v;
				}
			}
		}
		if (type === 'Scraper') {
			for (const name of META_FIELD_NAMES) {
				const sel = values[`meta.${name}`]?.trim();
				const attr = values[`meta.${name}.attr`]?.trim();
				const regex = values[`meta.${name}.regex`];
				const replacement = values[`meta.${name}.replacement`];
				if (sel) cfg[`meta.${name}`] = sel;
				if (attr) cfg[`meta.${name}.attr`] = attr;
				// Patterns and replacements keep leading/trailing whitespace — it is often
				// exactly what the pattern is meant to strip.
				if (regex) cfg[`meta.${name}.regex`] = regex;
				// Only meaningful alongside a pattern; empty replacement = delete the match.
				if (regex && replacement) cfg[`meta.${name}.replacement`] = replacement;
			}
		}
		return cfg;
	}

	async function save() {
		// Warn before dropping other users' subscriptions.
		if (mode === 'edit' && source!.visibility === 'Shared' && visibility === 'Private') {
			const ok = await confirmDialog(
				"Switching this source to Private will unsubscribe it from other users' playlists that follow it. Continue?"
			);
			if (!ok) return;
		}

		busy = true;
		error = null;
		try {
			const config = buildConfig();
			const filterBody = buildFilter();
			let res: Response;
			if (mode === 'create') {
				res = await api.post('/sources', { name, type, config, schedule, visibility, status, timeZone, filter: filterBody });
			} else {
				res = await api.patch(`/sources/${source!.id}`, { name, type, schedule, config, visibility, status, filter: filterBody });
			}
			if (!res.ok) {
				error =
					res.status === 429
						? 'You have reached your source quota.'
						: ((await problem(res)) ?? 'Could not save — check the name and config.');
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

	/**
	 * The server's own words, when it has any. A rejected pattern is worth quoting verbatim —
	 * "Could not save" tells someone nothing about which bracket they left open.
	 */
	async function problem(res: Response): Promise<string | null> {
		try {
			const body = (await res.json()) as { errors?: Record<string, string[]>; detail?: string };
			const first = Object.values(body.errors ?? {})[0]?.[0];
			return first ?? body.detail ?? null;
		} catch {
			return null;
		}
	}

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border-strong); background: var(--color-bg)';
</script>

{#snippet infoTip(text: string)}
	<span class="group relative inline-flex cursor-default">
		<Info size={12} aria-hidden="true" style="color: var(--color-muted)" />
		<span class="pointer-events-none absolute bottom-full left-1/2 z-10 mb-1.5 -translate-x-1/2 whitespace-nowrap rounded border px-2 py-1 text-xs opacity-0 shadow-sm transition-opacity group-hover:opacity-100" style="border-color: var(--color-border); background: var(--color-surface); color: var(--color-muted)">{text}</span>
	</span>
{/snippet}

<div class="flex flex-col gap-4">
	<div class="flex flex-col gap-1 text-sm">
		<!-- A label rather than a caption: the field used to have no name of its own, so a screen
		     reader announced it as nothing more than "edit text". -->
		<label for="{uid}-name">Name</label>
		<div class="flex items-center gap-2">
			<input id="{uid}-name" bind:value={name} class="{fieldClass} flex-1" style={fieldStyle} />
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

	<div class="flex flex-wrap items-end gap-8">
		<div class="flex flex-col gap-2 text-sm">
			<span>Run every</span>
			<div class="flex items-center gap-2">
				<div class="inline-flex divide-x overflow-hidden rounded-md border text-sm" style="border-color: var(--color-border); --tw-divide-opacity: 1">
					<button
						type="button"
						disabled={scheduleCount <= (scheduleUnit === 'minutes' ? 5 : 1)}
						onclick={() => scheduleCount--}
						class="px-2.5 py-2 hover:bg-black/5 dark:hover:bg-white/10 disabled:cursor-default disabled:opacity-30"
						style="background: var(--color-bg)"
					>−</button>
					<span class="flex min-w-[2.5rem] items-center justify-center px-2 py-2 tabular-nums" style="background: var(--color-bg)">{scheduleCount}</span>
					<button
						type="button"
						disabled={scheduleCount >= (scheduleUnit === 'minutes' ? 59 : scheduleUnit === 'hours' ? 23 : 30)}
						onclick={() => scheduleCount++}
						class="px-2.5 py-2 hover:bg-black/5 dark:hover:bg-white/10 disabled:cursor-default disabled:opacity-30"
						style="background: var(--color-bg)"
					>+</button>
				</div>
				<div class="inline-flex divide-x overflow-hidden rounded-md border text-sm" style="border-color: var(--color-border)">
					{#each [['minutes', 'min'], ['hours', 'hr'], ['days', 'day']] as [val, lbl] (val)}
						<button
							type="button"
							onclick={() => {
								scheduleUnit = val as 'minutes' | 'hours' | 'days';
								if (val === 'minutes' && scheduleCount < 5) scheduleCount = 5;
								if (val === 'hours' && scheduleCount > 23) scheduleCount = 23;
								if (val === 'days' && scheduleCount > 30) scheduleCount = 30;
							}}
							class="px-3 py-2 disabled:cursor-default"
							class:font-medium={scheduleUnit === val}
							style={scheduleUnit === val ? 'background: var(--color-selected); color: var(--color-accent)' : 'background: var(--color-bg)'}
						>{lbl}</button>
					{/each}
				</div>
			</div>
		</div>

		<div class="flex flex-col gap-2 text-sm">
			<span>Enabled</span>
			<Switch bind:checked={enabled} label="Run this source on its schedule" />
			{#if wasFailing && enabled}
				<span class="text-xs" style="color: var(--color-muted)">
					Saving clears the {source!.consecutiveFailures} failures and puts it back on schedule.
				</span>
			{:else if !enabled}
				<span class="text-xs" style="color: var(--color-muted)">Paused — runs only when you trigger one.</span>
			{/if}
		</div>
	</div>

	<fieldset class="rounded-lg border p-4" style="border-color: var(--color-border)">
		<legend class="px-1 text-xs" style="color: var(--color-muted)">Configuration</legend>

		<div class="flex flex-col gap-6">
			<!-- Type + main config fields -->
			<div class="flex flex-col gap-4">
				<label class="flex flex-col gap-1 text-sm">
					<span>Type</span>
					<select bind:value={type} class={fieldClass} style={fieldStyle}>
						<option value="Rss">RSS / Atom</option>
						<option value="Scraper">Web scraper</option>
						<option value="JsonApi">JSON API</option>
						<option value="Webhook">Webhook (push)</option>
					</select>
				</label>

				{#if type === 'Webhook'}
					<!-- Nothing to fill in. What this type needs is the URL, going the other way. -->
					<div class="rounded-md border p-3 text-sm" style="border-color: var(--color-border)">
						{#if webhookUrl}
							<p>Push links here:</p>
							<div class="mt-2 flex items-center gap-2">
								<code class="min-w-0 flex-1 truncate rounded px-2 py-1 text-xs" style="background: var(--color-bg)">{webhookUrl}</code>
								<button
									type="button"
									onclick={copyWebhookUrl}
									class="shrink-0 rounded-md border px-2.5 py-1 text-xs"
									style="border-color: var(--color-border)"
								>{copied ? 'Copied' : 'Copy'}</button>
							</div>
							<p class="mt-2 text-xs" style="color: var(--color-muted)">
								<code>POST</code> it <code>{'{ "links": [{ "url": "…", "title": "…" }] }'}</code>.
								The URL is the whole credential — treat it like a password.
							</p>
						{:else}
							<p style="color: var(--color-muted)">
								Save this source and it will be given a URL to push links to — for n8n, a Zap,
								a GitHub Action, or a one-line shell script.
							</p>
						{/if}
					</div>
				{/if}

				{#each FIELDS[type].filter(f => !SCRAPER_LINK_FIELDS.includes(f.key as typeof SCRAPER_LINK_FIELDS[number])) as f (f.key)}
					<label class="flex flex-col gap-1 text-sm">
						<span>{f.label}{#if !f.required && !f.hideOptional}<span style="color: var(--color-muted)"> (optional)</span>{/if}</span>
						<input bind:value={values[f.key]} type={f.inputType ?? 'text'} placeholder={f.placeholder ?? ''} class={fieldClass} style={fieldStyle} />
					</label>
				{/each}

				{#if type === 'Scraper'}
					<div class="flex gap-2">
						<label class="flex flex-1 flex-col gap-1 text-sm">
							<span class="inline-flex items-center gap-1">Link selector {@render infoTip('Selector is relative to the item selector')}</span>
							<input bind:value={values['linkSelector']} class={fieldClass} style={fieldStyle} />
						</label>
						<label class="flex flex-1 flex-col gap-1 text-sm">
							<span class="inline-flex items-center gap-1">Link attribute {@render infoTip('Leave blank to read text content')}</span>
							<input bind:value={values['linkAttribute']} class={fieldClass} style={fieldStyle} />
						</label>
					</div>
				{/if}
			</div>

			{#if type === 'JsonApi' || type === 'Scraper'}
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
							<input bind:value={header.name} class="{fieldClass} flex-1" style={fieldStyle} />
							<input bind:value={header.value} class="{fieldClass} flex-1" style={fieldStyle} />
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
								<button
									type="button"
									onclick={() => setAuthMode(opt.value as 'none' | 'loginUrl')}
									class="px-3 py-1.5"
									style={authMode === opt.value
										? 'background: var(--color-accent-solid); color: var(--color-on-solid)'
										: 'background: var(--color-bg)'}
									aria-pressed={authMode === opt.value}
								>{opt.label}</button>
							{/each}
						</div>
					</div>
					{#if authMode === 'loginUrl'}
						<label class="flex flex-col gap-1 text-sm">
							<span>Login URL</span>
							<input bind:value={values['auth.loginUrl']} class={fieldClass} style={fieldStyle} />
						</label>
						<div class="flex gap-2">
							<label class="flex flex-1 flex-col gap-1 text-sm">
								<span>Username</span>
								<input bind:value={values['auth.username']} class={fieldClass} style={fieldStyle} />
							</label>
							<label class="flex flex-1 flex-col gap-1 text-sm">
								<span>Password</span>
								<input bind:value={values['auth.password']} type="password" class={fieldClass} style={fieldStyle} />
							</label>
						</div>
					{/if}
				</div>

				<!-- Metadata (Scraper only) -->
				{#if type === 'Scraper'}
					<div class="flex flex-col gap-3 rounded-lg border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
						<span class="text-sm font-medium">Metadata</span>
						<div class="grid grid-cols-[4.5rem_1.4fr_0.8fr_1.4fr_1fr] items-center gap-x-2 gap-y-2 text-sm">
							<span class="text-xs font-medium" style="color: var(--color-muted)">Field</span>
							<span class="inline-flex items-center gap-1 text-xs font-medium" style="color: var(--color-muted)">Selector {@render infoTip('Selector is relative to the item selector')}</span>
							<span class="inline-flex items-center gap-1 text-xs font-medium" style="color: var(--color-muted)">Attribute {@render infoTip('Leave blank to read text content')}</span>
							<span class="inline-flex items-center gap-1 text-xs font-medium" style="color: var(--color-muted)">Pattern {@render infoTip('Regex — every match in the value is replaced')}</span>
							<span class="inline-flex items-center gap-1 text-xs font-medium" style="color: var(--color-muted)">Replacement {@render infoTip('Blank deletes the match; $1 inserts a capture group')}</span>
							{#each META_FIELD_NAMES as name (name)}
								<span class="capitalize" style="color: var(--color-muted)">{name}</span>
								<input bind:value={values[`meta.${name}`]} class={fieldClass} style={fieldStyle} />
								<input bind:value={values[`meta.${name}.attr`]} class={fieldClass} style={fieldStyle} />
								<input bind:value={values[`meta.${name}.regex`]} spellcheck="false" class="{fieldClass} font-mono" style={fieldStyle} />
								<input bind:value={values[`meta.${name}.replacement`]} spellcheck="false" class="{fieldClass} font-mono" style={fieldStyle} />
							{/each}
						</div>
						<span class="text-xs" style="color: var(--color-muted)">
							Pattern and replacement clean up the extracted value — e.g. <code>\s*\|\s*Site Name$</code> with a blank replacement strips a trailing “ | Site Name” from a title.
						</span>
					</div>
				{/if}
			{/if}
		</div>
	</fieldset>

	{#if previewing || preview || previewError}
		<div class="rounded-lg border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
			<div class="flex items-center justify-between">
				<span class="text-sm font-medium">What this finds now</span>
				{#if previewing}
					<span class="text-xs" style="color: var(--color-muted)">Checking…</span>
				{:else if preview}
					<span class="text-xs" style="color: var(--color-muted)">
						{preview.count} {preview.count === 1 ? 'link' : 'links'}
					</span>
				{/if}
			</div>

			{#if previewError}
				<p class="mt-2 text-sm" style="color: var(--color-danger)" role="alert">{previewError}</p>
			{:else if preview && preview.links.length === 0}
				<!-- The failure that looks like success: the fetch worked and matched nothing. -->
				<p class="mt-2 text-sm" style="color: var(--color-warning)">
					Read it, and found nothing. The selector probably doesn't match.
				</p>
			{:else if preview}
				<ul class="mt-2 flex flex-col gap-1 text-sm">
					{#each preview.links as link (link.url)}
						<li class="truncate" style="color: var(--color-muted)">
							{link.title ?? link.url}
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	{/if}

	<fieldset class="rounded-lg border p-4" style="border-color: var(--color-border)">
		<legend class="px-1 text-xs" style="color: var(--color-muted)">
			<button
				type="button"
				onclick={() => (filtersOpen = !filtersOpen)}
				class="inline-flex items-center gap-1 hover:opacity-70"
				aria-expanded={filtersOpen}
			>
				<ChevronRight
					size={12}
					aria-hidden="true"
					class="transition-transform duration-150"
					style={filtersOpen ? 'transform: rotate(90deg)' : ''}
				/>
				Filters{#if !filtersOpen && hasFilter(source?.filter)}<span style="color: var(--color-accent)"> · on</span>{/if}
			</button>
		</legend>

		{#if filtersOpen}
			<div class="flex flex-col gap-4">
				<p class="text-xs" style="color: var(--color-muted)">
					Without these, everything the source finds lands. Patterns are regular expressions and
					ignore case; leave a box empty to skip that rule.
				</p>

				<div class="grid gap-3 sm:grid-cols-2">
					<label class="flex flex-col gap-1 text-sm">
						<span class="inline-flex items-center gap-1">Title must match {@render infoTip('Links whose title does not match are skipped')}</span>
						<input bind:value={filter.titleInclude} spellcheck="false" class="{fieldClass} font-mono" style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>Title must not match</span>
						<input bind:value={filter.titleExclude} spellcheck="false" placeholder="sponsored|advertorial" class="{fieldClass} font-mono" style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>URL must match</span>
						<input bind:value={filter.urlInclude} spellcheck="false" class="{fieldClass} font-mono" style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>URL must not match</span>
						<input bind:value={filter.urlExclude} spellcheck="false" placeholder="/tag/|/author/" class="{fieldClass} font-mono" style={fieldStyle} />
					</label>
				</div>

				<div class="grid gap-3 sm:grid-cols-3">
					<label class="flex flex-col gap-1 text-sm">
						<span class="inline-flex items-center gap-1">Minimum age {@render infoTip('Hours. Only applies when the source reports a date')}</span>
						<input bind:value={filter.minAgeHours} type="number" min="0" max="720" placeholder="any" class={fieldClass} style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span class="inline-flex items-center gap-1">Most items a run {@render infoTip('Applied after the patterns, so the cap keeps what matched')}</span>
						<input bind:value={filter.maxItems} type="number" min="1" placeholder="no limit" class={fieldClass} style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span class="inline-flex items-center gap-1">Don't re-add for {@render infoTip('Days. Deleting something this source found otherwise lasts until its next run')}</span>
						<input bind:value={filter.dedupeWindowDays} type="number" min="0" max="30" placeholder="never" class={fieldClass} style={fieldStyle} />
					</label>
				</div>
			</div>
		{/if}
	</fieldset>

	<div class="flex items-center gap-3">
		<button
			type="button"
			onclick={save}
			disabled={busy}
			class="inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
			style="background: var(--color-accent-solid); color: var(--color-on-solid)"
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
			<!-- Announced: the button that failed is right next to it, but somebody not looking at
			     the screen had no way to know the save did not happen. -->
			<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
		{/if}
	</div>
</div>
