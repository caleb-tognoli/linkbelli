<script lang="ts">
	import { toast } from '$lib/toast.svelte';
	import CopyField from '$lib/components/ui/CopyField.svelte';
	import { buttonClass } from '$lib/components/ui/Button.svelte';
	import MenuRadio from '$lib/components/ui/MenuRadio.svelte';
	import Menu from '$lib/components/ui/Menu.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { beforeNavigate, goto, invalidateAll } from '$app/navigation';
	import { untrack } from 'svelte';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { X, Plus, Save, Lock, Globe, Trash2, ChevronRight, ChevronDown } from '@lucide/svelte';
	import Switch from './Switch.svelte';
	import DestinationPicker, { NEW_PLAYLIST, createPlaylist } from '$lib/components/DestinationPicker.svelte';
	import type { Playlist, Source, SourceFilter, SourceType, SourceVisibility } from '$lib/types';
	import { isPreviewable, previewKey, type SourcePreview } from '$lib/sourcePreview';

	type VisOption = { label: string; icon: typeof Lock };
	const visConfig: Record<SourceVisibility, VisOption> = {
		Private: { label: 'Private', icon: Lock },
		Shared: { label: 'Shared', icon: Globe }
	};

	let {
		mode,
		source,
		ondelete,
		playlists = [],
		preselectedPlaylistId = null
	}: {
		mode: 'create' | 'edit';
		source?: Source;
		ondelete?: () => void;
		/** Where a new source's links can land. Only used when creating one. */
		playlists?: Playlist[];
		preselectedPlaylistId?: string | null;
	} = $props();

	// Asked while the source is being made, not left to "Link playlist" on a page nobody knows
	// to look for afterwards.
	let destination = $state(preselectedPlaylistId ?? playlists[0]?.id ?? NEW_PLAYLIST);
	let newPlaylistName = $state('');

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

	/**
	 * Which field a complaint belongs to.
	 *
	 * Every refusal used to land in one line beside the Save button — "Could not save — check the
	 * name and config" — however specific the server had been about which field it meant.
	 */
	let fieldErrors = $state<Record<string, string>>({});

	/** Everything typed, as one comparable value, so leaving with unsaved edits can be noticed. */
	const snapshot = $derived(
		JSON.stringify({ name, type, values, headers, schedule, visibility, status, filter, authMode })
	);
	// What it looked like when it was last in step with the server: at first load, and again
	// after every successful save.
	let baseline = $state(untrack(() => snapshot));
	const dirty = $derived(snapshot !== baseline);
	// Set while this form is the one navigating, so its own redirect after Create is not treated
	// as somebody walking away from unsaved work.
	let leaving = $state(false);

	// The edits are in the browser and nowhere else. Cancelling a `leave` hands it to the
	// browser's own "leave site?" dialog, which is the only thing that can stop a tab closing.
	beforeNavigate((nav) => {
		if (!dirty || leaving || busy) return;
		if (nav.type === 'leave') {
			nav.cancel();
			return;
		}
		const to = nav.to?.url;
		if (!to) return;
		nav.cancel();
		void confirmDialog('Leave without saving?', {
			description: 'The changes to this source have not been saved.',
			danger: true,
			confirmLabel: 'Leave'
		}).then((ok) => {
			if (!ok) return;
			leaving = true;
			void goto(to);
		});
	});

	// Built here rather than server-side: the origin someone is looking at is the one that will
	// actually reach this instance.
	const webhookUrl = $derived(
		source?.webhookToken && typeof location !== 'undefined'
			? `${location.origin}/api/v1/hooks/${source.webhookToken}`
			: null
	);



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
					: (firstComplaint(await problem(res)) ?? 'Could not read that source.');
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

	/** The required fields of the current type that were left empty. */
	function missingFields(): Record<string, string> {
		const missing: Record<string, string> = {};
		if (!name.trim()) missing.name = 'Give this source a name.';
		if (mode === 'create' && destination === NEW_PLAYLIST && !newPlaylistName.trim()) {
			missing.newPlaylistName = 'Name the playlist this fills.';
		}
		for (const f of FIELDS[type]) {
			if (f.required && !values[f.key]?.trim()) missing[f.key] = `${f.label} is needed.`;
		}
		return missing;
	}

	async function save(event?: SubmitEvent) {
		event?.preventDefault();

		fieldErrors = missingFields();
		if (Object.keys(fieldErrors).length > 0) {
			error = null;
			// Straight to the first thing to fix, rather than leaving it to be hunted for.
			const first = Object.keys(fieldErrors)[0];
			document.getElementById(fieldId(first))?.focus();
			return;
		}

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
				const playlistId =
					destination === NEW_PLAYLIST ? await createPlaylist(newPlaylistName) : destination;
				if (!playlistId) {
					error = 'Could not make that playlist. The source was not created.';
					return;
				}
				res = await api.post('/sources', {
					name, type, config, schedule, visibility, status, timeZone,
					filter: filterBody,
					playlistIds: [playlistId]
				});
			} else {
				res = await api.patch(`/sources/${source!.id}`, { name, type, schedule, config, visibility, status, filter: filterBody });
			}
			if (!res.ok) {
				if (res.status === 429) {
					error = 'You have reached your source quota.';
					return;
				}
				const complaint = await problem(res);
				// Named fields go under the field; anything else stays beside the button.
				fieldErrors = complaint.byField;
				error =
					Object.keys(complaint.byField).length > 0
						? null
						: (complaint.message ?? 'Could not save — check the name and config.');
				if (Object.keys(complaint.byField).length > 0) {
					document.getElementById(fieldId(Object.keys(complaint.byField)[0]))?.focus();
				}
				return;
			}
			fieldErrors = {};
			baseline = snapshot;
			if (mode === 'create') {
				const created = (await res.json()) as Source;
				leaving = true;
				await goto(`/sources/${created.id}`);
			} else {
				await invalidateAll();
				toast.success('Saved.');
			}
		} finally {
			busy = false;
		}
	}

	/** A complaint in one line, for the preview panel, which has no fields to hang them on. */
	function firstComplaint(complaint: { byField: Record<string, string>; message: string | null }): string | null {
		return complaint.message ?? Object.values(complaint.byField)[0] ?? null;
	}

	/** The id of a field's input, so a complaint about it can put the cursor there. */
	function fieldId(key: string): string {
		return key === 'name' ? `${uid}-name` : `${uid}-cfg-${key}`;
	}

	/**
	 * The server's own words, when it has any, against the fields they are about.
	 *
	 * A rejected pattern is worth quoting verbatim — "Could not save" tells someone nothing about
	 * which bracket they left open — and ProblemDetails already says which key it means, which
	 * this page used to throw away.
	 */
	async function problem(res: Response): Promise<{ byField: Record<string, string>; message: string | null }> {
		try {
			const body = (await res.json()) as { errors?: Record<string, string[]>; detail?: string };
			const byField: Record<string, string> = {};
			const known = ['name', ...FIELDS[type].map((f) => f.key), ...AUTH_FIELDS.map((f) => f.key)];
			let message: string | null = null;
			for (const [key, messages] of Object.entries(body.errors ?? {})) {
				// "Config.feedUrl" and "feedUrl" both mean the feed URL box.
				const tail = key.split('.').slice(-1)[0];
				const match = known.find((k) => k.split('.').slice(-1)[0].toLowerCase() === tail.toLowerCase());
				if (match) byField[match] = messages[0];
				else message ??= messages[0];
			}
			return { byField, message: message ?? body.detail ?? null };
		} catch {
			return { byField: {}, message: null };
		}
	}

</script>

<!-- A real form, so Enter in a text box saves; it was a div, and Enter did nothing. `novalidate`
     because the missing fields are said in the page, under the field, in the same voice as the
     rest — not in a browser bubble that disappears on the next click. -->
<form class="flex flex-col gap-4" novalidate onsubmit={save}>
	<div class="flex flex-col gap-1 text-sm">
		<!-- A label rather than a caption: the field used to have no name of its own, so a screen
		     reader announced it as nothing more than "edit text". -->
		<label for="{uid}-name">
			Name <span aria-hidden="true" class="text-danger">*</span>
		</label>
		<div class="flex items-center gap-2">
			<Input
				id="{uid}-name"
				bind:value={name}
				required
				invalid={!!fieldErrors.name}
				aria-describedby={fieldErrors.name ? `${uid}-name-error` : undefined}
				class="flex-1"
			/>
			<Menu
				triggerClass={buttonClass('secondary', 'md', false, 'shrink-0')}
				title="Change visibility"
				align="end"
				width="w-64"
			>
				{#snippet trigger()}
					<currentVis.icon size={14} aria-hidden="true" />
					<span class="sr-only">Visibility:</span>
					{currentVis.label}
					<ChevronDown size={13} aria-hidden="true" />
				{/snippet}
				<MenuRadio
					value={visibility}
					options={[
						{ value: 'Private', label: 'Private', icon: Lock, description: 'Only your playlists can use it' },
						{ value: 'Shared', label: 'Shared', icon: Globe, description: 'Anyone can find it and attach it to their playlists' }
					]}
					onchange={(next) => (visibility = next as SourceVisibility)}
				/>
			</Menu>
		</div>
		{#if fieldErrors.name}
			<p id="{uid}-name-error" class="text-xs text-danger" role="alert">{fieldErrors.name}</p>
		{/if}
	</div>

	{#if mode === 'create'}
		<DestinationPicker
			{playlists}
			bind:value={destination}
			bind:newName={newPlaylistName}
			error={fieldErrors.newPlaylistName ?? null}
		/>
	{/if}

	<div class="flex flex-wrap items-end gap-8">
		<div class="flex flex-col gap-2 text-sm">
			<span>Run every</span>
			<div class="flex items-center gap-2">
				<div class="inline-flex divide-x overflow-hidden rounded-control border text-sm" style="border-color: var(--color-border); --tw-divide-opacity: 1">
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
				<SegmentedControl
					label="Unit"
					options={[
						{ value: 'minutes', label: 'min' },
						{ value: 'hours', label: 'hr' },
						{ value: 'days', label: 'day' }
					]}
					bind:value={scheduleUnit}
					onchange={(unit) => {
						if (unit === 'minutes' && scheduleCount < 5) scheduleCount = 5;
						if (unit === 'hours' && scheduleCount > 23) scheduleCount = 23;
						if (unit === 'days' && scheduleCount > 30) scheduleCount = 30;
					}}
				/>
			</div>
		</div>

		<div class="flex flex-col gap-2 text-sm">
			<span id="{uid}-enabled">Run on its schedule</span>
			<Switch bind:checked={enabled} labelledby="{uid}-enabled" />
			{#if wasFailing && enabled}
				<span class="text-xs" style="color: var(--color-muted)">
					Saving clears the {source!.consecutiveFailures} failures and puts it back on schedule.
				</span>
			{:else if !enabled}
				<span class="text-xs" style="color: var(--color-muted)">Paused — runs only when you trigger one.</span>
			{/if}
		</div>
	</div>

	<fieldset class="rounded-card border p-4" style="border-color: var(--color-border)">
		<legend class="px-1 text-xs" style="color: var(--color-muted)">Configuration</legend>

		<div class="flex flex-col gap-6">
			<!-- Type + main config fields -->
			<div class="flex flex-col gap-4">
				<Field label="Type">
					{#snippet children(f)}
						<Select
							id={f.id}
							bind:value={type}
							aria-describedby={f.describedby}
						>
						<option value="Rss">RSS / Atom</option>
						<option value="Scraper">Web scraper</option>
						<option value="JsonApi">JSON API</option>
						<option value="Webhook">Webhook (push)</option>
						</Select>
					{/snippet}
				</Field>

				{#if type === 'Webhook'}
					<!-- Nothing to fill in. What this type needs is the URL, going the other way. -->
					<div class="rounded-control border p-3 text-sm" style="border-color: var(--color-border)">
						{#if webhookUrl}
							<p>Push links here:</p>
							<CopyField value={webhookUrl} label="Webhook address" class="mt-2" />
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
					<Field
						label={f.label}
						id={fieldId(f.key)}
						required={f.required}
						optional={!f.required && !f.hideOptional}
						error={fieldErrors[f.key] ?? null}
					>
						{#snippet children(c)}
							<Input
								id={c.id}
								bind:value={values[f.key]}
								type={f.inputType ?? 'text'}
								placeholder={f.placeholder ?? ''}
								required={f.required}
								invalid={c.invalid}
								aria-describedby={c.describedby}
							/>
						{/snippet}
					</Field>
				{/each}

				{#if type === 'Scraper'}
					<div class="grid gap-3 sm:grid-cols-2">
						<Field label="Link selector" hint="Inside each item. Leave blank to use the item itself.">
							{#snippet children(f)}
								<Input id={f.id} aria-describedby={f.describedby} bind:value={values['linkSelector']} />
							{/snippet}
						</Field>
						<Field label="Link attribute" hint="Leave blank to read the element's text.">
							{#snippet children(f)}
								<Input id={f.id} aria-describedby={f.describedby} bind:value={values['linkAttribute']} />
							{/snippet}
						</Field>
					</div>
				{/if}
			</div>

			{#if type === 'JsonApi' || type === 'Scraper'}
				<!-- Request headers -->
				<div class="flex flex-col gap-2">
					<div class="flex items-center justify-between">
						<span class="text-sm">Request headers</span>
						<button type="button" onclick={() => (headers = [...headers, { name: '', value: '' }])} class="inline-flex items-center rounded-control p-1.5 hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-accent)" title="Add request header" aria-label="Add request header">
							<Plus size={15} aria-hidden="true" />
						</button>
					</div>
					{#each headers as header, i (i)}
						<!-- Name over value on a phone: side by side they were about seventy pixels
						     each, which shows neither. -->
						<div class="flex flex-wrap items-start gap-2 sm:flex-nowrap">
							<Input bind:value={header.name} placeholder="Name" aria-label="Header name" class="min-w-[9rem] flex-1" />
							<Input bind:value={header.value} placeholder="Value" aria-label="Header value" class="min-w-[9rem] flex-1" />
							<Button
								variant="ghost-danger"
								size="sm"
								icon={X}
								iconOnly
								label={`Remove header ${header.name || i + 1}`}
								onclick={() => (headers = headers.filter((_, j) => j !== i))}
							/>
						</div>
					{/each}
				</div>

				<!-- Authentication -->
				<div class="flex flex-col gap-3 rounded-card border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
					<div class="flex items-center justify-between gap-4">
						<span class="text-sm font-medium">Authentication</span>
						<SegmentedControl
							label="Authentication"
							options={[
								{ value: 'none', label: 'None' },
								{ value: 'loginUrl', label: 'Login URL' }
							]}
							value={authMode}
							onchange={setAuthMode}
						/>
					</div>
					{#if authMode === 'loginUrl'}
						<Field label="Login URL">
							{#snippet children(f)}
								<Input
									id={f.id}
									bind:value={values['auth.loginUrl']}
									aria-describedby={f.describedby}
								/>
							{/snippet}
						</Field>
						<div class="grid gap-2 sm:grid-cols-2">
							<Field label="Username" class="flex-1">
								{#snippet children(f)}
									<Input
										id={f.id}
										bind:value={values['auth.username']}
										aria-describedby={f.describedby}
									/>
								{/snippet}
							</Field>
							<Field label="Password" class="flex-1">
								{#snippet children(f)}
									<Input
										id={f.id}
										bind:value={values['auth.password']}
										type="password"
										aria-describedby={f.describedby}
									/>
								{/snippet}
							</Field>
						</div>
					{/if}
				</div>

				<!-- Metadata (Scraper only) -->
				{#if type === 'Scraper'}
					<div class="flex flex-col gap-3 rounded-card border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
						<span class="text-sm font-medium">Metadata</span>
						<!-- Stacked into a card per field below `md`: as a five-column grid on a phone
						     each input was about fifty pixels wide, which is unusable for a CSS
						     selector or a regular expression. -->
						<div class="hidden md:grid md:grid-cols-[4.5rem_1.4fr_0.8fr_1.4fr_1fr] md:items-center md:gap-x-2 md:gap-y-2 md:text-sm">
							<span class="text-xs font-medium" style="color: var(--color-muted)">Field</span>
							<span class="text-xs font-medium" style="color: var(--color-muted)">Selector</span>
							<span class="text-xs font-medium" style="color: var(--color-muted)">Attribute</span>
							<span class="text-xs font-medium" style="color: var(--color-muted)">Pattern</span>
							<span class="text-xs font-medium" style="color: var(--color-muted)">Replacement</span>
							{#each META_FIELD_NAMES as name (name)}
								<span class="capitalize" style="color: var(--color-muted)">{name}</span>
								<Input bind:value={values[`meta.${name}`]} aria-label={`${name} selector`} />
								<Input bind:value={values[`meta.${name}.attr`]} aria-label={`${name} attribute`} />
								<Input
									bind:value={values[`meta.${name}.regex`]}
									aria-label={`${name} pattern`}
									spellcheck="false"
									class="font-mono"
								/>
								<Input
									bind:value={values[`meta.${name}.replacement`]}
									aria-label={`${name} replacement`}
									spellcheck="false"
									class="font-mono"
								/>
							{/each}
						</div>

						<div class="flex flex-col gap-4 md:hidden">
							{#each META_FIELD_NAMES as name (name)}
								<fieldset class="flex flex-col gap-2 rounded-card border p-3">
									<legend class="px-1 text-xs font-medium capitalize" style="color: var(--color-muted)">
										{name}
									</legend>
									<Field label="Selector">
										{#snippet children(f)}
											<Input id={f.id} bind:value={values[`meta.${name}`]} aria-describedby={f.describedby} />
										{/snippet}
									</Field>
									<Field label="Attribute">
										{#snippet children(f)}
											<Input id={f.id} bind:value={values[`meta.${name}.attr`]} aria-describedby={f.describedby} />
										{/snippet}
									</Field>
									<Field label="Pattern">
										{#snippet children(f)}
											<Input
												id={f.id}
												bind:value={values[`meta.${name}.regex`]}
												spellcheck="false"
												class="font-mono"
												aria-describedby={f.describedby}
											/>
										{/snippet}
									</Field>
									<Field label="Replacement">
										{#snippet children(f)}
											<Input
												id={f.id}
												bind:value={values[`meta.${name}.replacement`]}
												spellcheck="false"
												class="font-mono"
												aria-describedby={f.describedby}
											/>
										{/snippet}
									</Field>
								</fieldset>
							{/each}
						</div>
						<!-- What used to hide in hover-only tips beside each column heading, where a
						     keyboard, a phone or a screen reader could never reach it. -->
						<p class="text-xs" style="color: var(--color-muted)">
							Selectors are looked for inside each item. Leave Attribute blank to read the element's
							text. Pattern is a regular expression, and every match in the value is replaced with
							Replacement — blank deletes it, <code>$1</code> puts back a captured group. For example
							<code>\s*\|\s*Site Name$</code> with a blank replacement strips a trailing
							“ | Site Name” from a title.
						</p>
					</div>
				{/if}
			{/if}
		</div>
	</fieldset>

	{#if previewing || preview || previewError}
		<div class="rounded-card border p-4" style="border-color: var(--color-border); background: var(--color-surface)">
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

	<fieldset class="rounded-card border p-4" style="border-color: var(--color-border)">
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
					<Field label="Title must match" hint="Links whose title does not match are skipped.">
						{#snippet children(f)}
							<Input id={f.id} aria-describedby={f.describedby} bind:value={filter.titleInclude} spellcheck="false" class="font-mono" />
						{/snippet}
					</Field>
					<Field label="Title must not match">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={filter.titleExclude}
								spellcheck="false"
								placeholder="sponsored|advertorial"
								class="font-mono"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<Field label="URL must match">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={filter.urlInclude}
								spellcheck="false"
								class="font-mono"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<Field label="URL must not match">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={filter.urlExclude}
								spellcheck="false"
								placeholder="/tag/|/author/"
								class="font-mono"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
				</div>

				<div class="grid gap-3 sm:grid-cols-3">
					<Field label="Minimum age (hours)" hint="Only where the source says when a link was published.">
						{#snippet children(f)}
							<Input id={f.id} aria-describedby={f.describedby} bind:value={filter.minAgeHours} type="number" min={0} max={720} placeholder="any" />
						{/snippet}
					</Field>
					<Field label="Most links a run" hint="Counted after the patterns, so the cap keeps what matched.">
						{#snippet children(f)}
							<Input id={f.id} aria-describedby={f.describedby} bind:value={filter.maxItems} type="number" min={1} placeholder="no limit" />
						{/snippet}
					</Field>
					<Field label="Don't re-add for (days)" hint="Otherwise a link you delete comes back on the next run.">
						{#snippet children(f)}
							<Input id={f.id} aria-describedby={f.describedby} bind:value={filter.dedupeWindowDays} type="number" min={0} max={30} placeholder="never" />
						{/snippet}
					</Field>
				</div>
			</div>
		{/if}
	</fieldset>

	<div class="flex items-center gap-3">
		<Button type="submit" variant="primary" icon={Save} loading={busy}>
			{mode === 'create' ? 'Create' : 'Save'}
		</Button>
		{#if ondelete}
			<Button variant="ghost-danger" icon={Trash2} onclick={ondelete} disabled={busy}>Delete</Button>
		{/if}
		{#if error}
			<!-- Announced: the button that failed is right next to it, but somebody not looking at
			     the screen had no way to know the save did not happen. -->
			<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
		{/if}
	</div>
</form>
