<script lang="ts">
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import { toast } from '$lib/toast.svelte';
	import Checkbox from '$lib/components/ui/Checkbox.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import Switch from '$lib/components/Switch.svelte';
	import { describeRule } from '$lib/automation';
	import { Check, History, Plus, Trash2, Wand2, X } from '@lucide/svelte';
	import type { AutomationPreview, AutomationRule, ContentKind } from '$lib/types';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const kinds: { value: ContentKind | ''; label: string }[] = [
		{ value: '', label: 'Anything' },
		{ value: 'Article', label: 'Articles' },
		{ value: 'Video', label: 'Videos' },
		{ value: 'Repository', label: 'Repositories' },
		{ value: 'Paper', label: 'Papers' },
		{ value: 'Document', label: 'Documents' },
		{ value: 'Audio', label: 'Audio' },
		{ value: 'Social', label: 'Posts' }
	];

	/** The rule being written. Blank fields are conditions that are simply not applied. */
	function blank() {
		return {
			name: '',
			playlistId: '',
			host: '',
			titlePattern: '',
			urlPattern: '',
			kind: '' as ContentKind | '',
			minMinutes: '',
			maxMinutes: '',
			broken: '' as '' | 'yes' | 'no',
			addTags: '',
			destination: '' as '' | 'move' | 'copy',
			destinationId: '',
			markWatched: false,
			trash: false,
			setScore: '',
			archive: false,
			stopOnMatch: false
		};
	}

	let draft = $state(blank());
	let editing = $state<string | null>(null);
	let open = $state(false);
	let busy = $state(false);

	let error = $state<string | null>(null);
	let preview = $state<AutomationPreview | null>(null);

	const playlistName = $derived((id: string | null) =>
		data.playlists.find((playlist) => playlist.id === id)?.name ?? 'a playlist'
	);

	/** A number, or null for an empty box. Empty means "no condition", never zero. */
	function whole(value: string): number | null {
		const trimmed = value.trim();
		if (!trimmed) return null;

		const parsed = Number(trimmed);
		return Number.isFinite(parsed) ? Math.round(parsed) : null;
	}

	function body() {
		return {
			name: draft.name,
			playlistId: draft.playlistId || null,
			host: draft.host.trim() || null,
			titlePattern: draft.titlePattern.trim() || null,
			urlPattern: draft.urlPattern.trim() || null,
			kind: draft.kind || null,
			// Empty is "no condition", which is not the same as zero — hence the parse rather
			// than a Number() that turns '' into 0 and files everything as instant reading.
			minMinutes: whole(draft.minMinutes),
			maxMinutes: whole(draft.maxMinutes),
			broken: draft.broken === '' ? null : draft.broken === 'yes',
			addTags: draft.addTags
				.split(',')
				.map((tag) => tag.trim())
				.filter(Boolean),
			moveToPlaylistId: draft.destination === 'move' ? draft.destinationId || null : null,
			copyToPlaylistId: draft.destination === 'copy' ? draft.destinationId || null : null,
			markWatched: draft.markWatched,
			trash: draft.trash,
			setScore: whole(draft.setScore),
			archive: draft.archive,
			stopOnMatch: draft.stopOnMatch
		};
	}

	function startNew() {
		draft = blank();
		editing = null;
		preview = null;
		error = null;
		open = true;
	}

	function startEdit(rule: AutomationRule) {
		draft = {
			name: rule.name,
			playlistId: rule.playlistId ?? '',
			host: rule.host ?? '',
			titlePattern: rule.titlePattern ?? '',
			urlPattern: rule.urlPattern ?? '',
			kind: rule.kind ?? '',
			minMinutes: rule.minMinutes?.toString() ?? '',
			maxMinutes: rule.maxMinutes?.toString() ?? '',
			broken: rule.broken === null || rule.broken === undefined ? '' : rule.broken ? 'yes' : 'no',
			addTags: rule.addTags.join(', '),
			destination: rule.moveToPlaylistId ? 'move' : rule.copyToPlaylistId ? 'copy' : '',
			destinationId: rule.moveToPlaylistId ?? rule.copyToPlaylistId ?? '',
			markWatched: rule.markWatched,
			trash: rule.trash,
			setScore: rule.setScore?.toString() ?? '',
			archive: rule.archive ?? false,
			stopOnMatch: rule.stopOnMatch
		};
		editing = rule.id;
		preview = null;
		error = null;
		open = true;
	}

	/** The server's own words when it has any — a rejected pattern is worth quoting verbatim. */
	async function problem(res: Response): Promise<string> {
		try {
			const parsed = (await res.json()) as { errors?: Record<string, string[]>; detail?: string };
			return Object.values(parsed.errors ?? {})[0]?.[0] ?? parsed.detail ?? 'Could not save that rule.';
		} catch {
			return 'Could not save that rule.';
		}
	}

	async function save() {
		if (!draft.name.trim()) {
			error = 'Give the rule a name.';
			return;
		}

		busy = true;
		error = null;
		try {
			const res = editing
				? await api.patch(`/automations/${editing}`, {
						...body(),
						// Null means "leave it alone" on a PATCH, so emptying a box has to say so.
						clear: clearedFields()
					})
				: await api.post('/automations', body());

			if (!res.ok) {
				error = await problem(res);
				return;
			}

			open = false;
			await invalidateAll();
		} finally {
			busy = false;
		}
	}

	/** Conditions the person emptied, which a PATCH cannot express with a null. */
	function clearedFields(): string[] {
		const cleared: string[] = [];
		if (!draft.playlistId) cleared.push('playlistId');
		if (!draft.host.trim()) cleared.push('host');
		if (!draft.titlePattern.trim()) cleared.push('titlePattern');
		if (!draft.urlPattern.trim()) cleared.push('urlPattern');
		if (!draft.kind) cleared.push('kind');
		if (!draft.minMinutes.trim()) cleared.push('minMinutes');
		if (!draft.maxMinutes.trim()) cleared.push('maxMinutes');
		if (!draft.broken) cleared.push('broken');
		if (!draft.setScore.trim()) cleared.push('setScore');
		if (!draft.addTags.trim()) cleared.push('addTags');
		if (draft.destination !== 'move') cleared.push('moveToPlaylistId');
		if (draft.destination !== 'copy') cleared.push('copyToPlaylistId');
		return cleared;
	}

	async function tryIt() {
		busy = true;
		error = null;
		try {
			const res = await api.post('/automations/preview', body());
			if (res.ok) {
				preview = (await res.json()) as AutomationPreview;
			} else {
				error = await problem(res);
			}
		} finally {
			busy = false;
		}
	}

	async function toggle(rule: AutomationRule, enabled: boolean) {
		const res = await api.patch(`/automations/${rule.id}`, { enabled });
		if (res.ok) await invalidateAll();
	}

	/** The rule currently being run over the backlog, so its button can say so. */
	let running = $state<string | null>(null);

	/**
	 * Runs a rule over the links already in the library.
	 *
	 * Confirmed first, and not because it is slow. A rule's actions include moving and trashing,
	 * and this is the one place they happen to things somebody has already filed by hand — so
	 * the number it is about to touch is worth seeing before it does.
	 */
	async function runOverExisting(rule: AutomationRule) {
		if (
			!(await confirmDialog(
				`Run "${rule.name}" over the links you already have? It applies the same actions ` +
					`it would to something arriving now, to everything it matches.`,
				{ confirmLabel: 'Run it' }
			))
		) {
			return;
		}

		running = rule.id;
		try {
			const res = await api.post(`/automations/${rule.id}/run`, {});
			if (!res.ok) {
				toast.error('Could not run that rule.');
				return;
			}

			const { acted } = (await res.json()) as { acted: number };
			toast.success(
				acted === 0
					? `"${rule.name}" matched nothing already here.`
					: `"${rule.name}" acted on ${acted} ${acted === 1 ? 'link' : 'links'}.`
			);
			await invalidateAll();
		} finally {
			running = null;
		}
	}

	async function remove(rule: AutomationRule) {
		if (!(await confirmDialog(`Delete "${rule.name}"? Items it has already filed stay where they are.`, {
			danger: true,
			confirmLabel: 'Delete'
		}))) {
			return;
		}

		const res = await api.del(`/automations/${rule.id}`);
		if (res.ok || res.status === 204) await invalidateAll();
	}

</script>

<svelte:head><title>Rules - linkbelli</title></svelte:head>

<Page width="narrow">
	<PageHeader
		title="Rules"
		description="What should happen to a link when it arrives. Rules run in order, top first, on everything that arrives from now on — and you can run one over the links you already have."
	>
		{#snippet actions()}
			<Button variant="primary" icon={Plus} onclick={startNew}>New rule</Button>
		{/snippet}
	</PageHeader>


	{#if data.rules.length === 0}
		<p class="mt-8 rounded-lg border px-4 py-8 text-center text-sm"
		   style="border-color: var(--color-border); color: var(--color-muted)">
			No rules yet. A first one might tag everything from one site, or send anything over
			twenty minutes to a "later" list.
		</p>
	{:else}
		<ul class="mt-6 flex flex-col gap-2">
			{#each data.rules as rule (rule.id)}
				<li
					class="rounded-lg border px-4 py-3"
					style="border-color: var(--color-border); background: var(--color-surface)"
				>
					<div class="flex items-start justify-between gap-3">
						<button type="button" onclick={() => startEdit(rule)} class="min-w-0 flex-1 text-left">
							<span class="font-medium" class:opacity-50={!rule.enabled}>{rule.name}</span>
							<p class="mt-0.5 text-sm" style="color: var(--color-muted)">
								{describeRule(rule, playlistName)}
							</p>
							<p class="mt-1 text-xs" style="color: var(--color-muted)">
								{#if rule.matchCount === 0}
									<!-- Nothing else on the page would ever say a rule doesn't work. -->
									Hasn't matched anything yet
								{:else}
									Acted on {rule.matchCount} {rule.matchCount === 1 ? 'link' : 'links'}
									· last {new Date(rule.lastMatchedAt!).toLocaleDateString()}
								{/if}
							</p>
						</button>
						<div class="flex shrink-0 items-center gap-2">
							<button
								type="button"
								onclick={() => runOverExisting(rule)}
								disabled={running === rule.id}
								class="rounded p-1.5 hover:bg-black/5 disabled:opacity-50 dark:hover:bg-white/10"
								style="color: var(--color-muted)"
								title="Run this over the links you already have"
								aria-label={`Run ${rule.name} over existing links`}
							>
								<History size={15} aria-hidden="true" />
							</button>
							<Switch checked={rule.enabled} onchange={(value) => toggle(rule, value)} label={`Enable ${rule.name}`} />
							<button
								type="button"
								onclick={() => remove(rule)}
								class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
								style="color: var(--color-danger)"
								title="Delete rule"
								aria-label="Delete rule"
							>
								<Trash2 size={16} aria-hidden="true" />
							</button>
						</div>
					</div>
				</li>
			{/each}
		</ul>
	{/if}

	{#if open}
		<div
			class="mt-8 rounded-lg border p-4"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-center justify-between">
				<h2 class="font-medium">{editing ? 'Edit rule' : 'New rule'}</h2>
				<button
					type="button"
					onclick={() => (open = false)}
					class="rounded p-1 hover:bg-black/5 dark:hover:bg-white/10"
					title="Close"
					aria-label="Close"
				>
					<X size={17} aria-hidden="true" />
				</button>
			</div>

			<label class="mt-4 flex flex-col gap-1 text-sm">
				<span>Name</span>
				<Input bind:value={draft.name} placeholder="Long reads go to Later" />
			</label>

			<fieldset class="mt-4 rounded-md border p-3" style="border-color: var(--color-border)">
				<legend class="px-1 text-xs" style="color: var(--color-muted)">When all of these are true</legend>
				<div class="grid gap-3 sm:grid-cols-2">
					<Field label="It lands in">
						{#snippet children(f)}
							<Select
								id={f.id}
								bind:value={draft.playlistId}
								aria-describedby={f.describedby}
							>
							<option value="">Any playlist</option>
							{#each data.playlists as playlist (playlist.id)}
								<option value={playlist.id}>{playlist.name}</option>
							{/each}
							</Select>
						{/snippet}
					</Field>
					<Field label="It is">
						{#snippet children(f)}
							<Select
								id={f.id}
								bind:value={draft.kind}
								aria-describedby={f.describedby}
							>
							{#each kinds as option (option.value)}
								<option value={option.value}>{option.label}</option>
							{/each}
							</Select>
						{/snippet}
					</Field>
					<Field label="From the site">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={draft.host}
								placeholder="example.com"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<Field label="Title matches">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={draft.titlePattern}
								spellcheck="false"
								placeholder="rust|zig"
								class="font-mono"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<Field label="Address matches" class="sm:col-span-2">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={draft.urlPattern}
								spellcheck="false"
								placeholder="/blog/"
								class="font-mono"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<label class="flex flex-col gap-1 text-sm">
						<!-- Search could ask this from the day it shipped, off the same stored
						     word count; rules could not. -->
						<span>Takes at least (minutes)</span>
						<Input
							bind:value={draft.minMinutes}
							type="number"
							min={1}
							placeholder="20"
						/>
					</label>
					<Field label="And at most (minutes)">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={draft.maxMinutes}
								type="number"
								min={1}
								placeholder="5"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<Field label="The page" class="sm:col-span-2">
						{#snippet children(f)}
							<Select
								id={f.id}
								bind:value={draft.broken}
								aria-describedby={f.describedby}
							>
							<option value="">Working or not, either way</option>
							<option value="yes">Has gone, or cannot be read</option>
							<option value="no">Is still there</option>
							</Select>
						{/snippet}
					</Field>
				</div>
				<p class="mt-2 text-xs" style="color: var(--color-muted)">
					Patterns are regular expressions and ignore case. Leave a box empty to skip that condition
					— a rule with none matches everything that arrives. A length condition only ever matches
					something with an article behind it: a video is not a short read.
				</p>
			</fieldset>

			<fieldset class="mt-4 rounded-md border p-3" style="border-color: var(--color-border)">
				<legend class="px-1 text-xs" style="color: var(--color-muted)">Then</legend>
				<div class="grid gap-3 sm:grid-cols-2">
					<Field label="Tag it">
						{#snippet children(f)}
							<Input
								id={f.id}
								bind:value={draft.addTags}
								placeholder="rust, later"
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
					<div class="flex gap-2">
						<Field label="And" class="flex-1">
							{#snippet children(f)}
								<Select
									id={f.id}
									bind:value={draft.destination}
									aria-describedby={f.describedby}
								>
								<option value="">Leave it where it is</option>
								<option value="move">Move it to</option>
								<option value="copy">Also put it in</option>
								</Select>
							{/snippet}
						</Field>
						{#if draft.destination}
							<label class="flex flex-1 flex-col gap-1 text-sm">
								<span class="sr-only">Destination playlist</span>
								<span aria-hidden="true">&nbsp;</span>
								<Select bind:value={draft.destinationId}>
									<option value="">Choose…</option>
									{#each data.playlists as playlist (playlist.id)}
										<option value={playlist.id}>{playlist.name}</option>
									{/each}
								</Select>
							</label>
						{/if}
					</div>
				</div>

				<div class="mt-3 grid gap-3 sm:grid-cols-2">
					<label class="flex flex-col gap-1 text-sm">
						<!-- The queue sorts on score, so this is how a rule says "this source is
						     worth my time" without rating every item by hand. -->
						<span>Score it</span>
						<Input
							bind:value={draft.setScore}
							type="number"
							min={0}
							max={100}
							placeholder="Leave it unrated"
						/>
					</label>
				</div>

				<div class="mt-3 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
					<Checkbox bind:checked={draft.markWatched}>Mark it watched</Checkbox>
					<Checkbox bind:checked={draft.archive}>Keep a public snapshot</Checkbox>
					<Checkbox bind:checked={draft.trash}>Send it to the trash</Checkbox>
					<Checkbox bind:checked={draft.stopOnMatch}>Stop here</Checkbox>
				</div>
				<p class="mt-2 text-xs" style="color: var(--color-muted)">
					"Stop here" keeps the rules below from seeing the item — which is how a specific rule
					shields something from a broad one underneath it. Trashed items are recoverable.
					A snapshot is asked of the Internet Archive, which means telling them the address.
				</p>
			</fieldset>

			<div class="mt-4 flex flex-wrap items-center gap-3">
				<Button variant="primary" icon={Check} onclick={save} disabled={busy}>
					{editing ? 'Save' : 'Create'}
				</Button>

				<!-- A rule only ever acts on what arrives next, so this is the only way to find out
				     whether it works without waiting to see what it does. -->
				<Button icon={Wand2} onclick={tryIt} disabled={busy}>Try it on what I have</Button>

				{#if error}
					<p class="text-sm" style="color: var(--color-danger)" role="alert">{error}</p>
				{/if}
			</div>

			{#if preview}
				<div class="mt-4 rounded-md border p-3 text-sm" style="border-color: var(--color-border)">
					<p>
						Would have matched <strong>{preview.matches}</strong>
						{preview.matches === 1 ? 'link' : 'links'} among your most recent saves.
					</p>
					{#if preview.sample.length}
						<ul class="mt-2 flex flex-col gap-1" style="color: var(--color-muted)">
							{#each preview.sample as item (item.itemId)}
								<li class="truncate">{item.title ?? item.url} <span class="text-xs">· {item.playlistName}</span></li>
							{/each}
						</ul>
					{/if}
				</div>
			{/if}
		</div>
	{/if}
</Page>
