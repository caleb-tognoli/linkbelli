<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import Switch from '$lib/components/Switch.svelte';
	import { describeRule } from '$lib/automation';
	import { Plus, Trash2, Wand2, X } from '@lucide/svelte';
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
			addTags: '',
			destination: '' as '' | 'move' | 'copy',
			destinationId: '',
			markWatched: false,
			trash: false,
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

	function body() {
		return {
			name: draft.name,
			playlistId: draft.playlistId || null,
			host: draft.host.trim() || null,
			titlePattern: draft.titlePattern.trim() || null,
			urlPattern: draft.urlPattern.trim() || null,
			kind: draft.kind || null,
			addTags: draft.addTags
				.split(',')
				.map((tag) => tag.trim())
				.filter(Boolean),
			moveToPlaylistId: draft.destination === 'move' ? draft.destinationId || null : null,
			copyToPlaylistId: draft.destination === 'copy' ? draft.destinationId || null : null,
			markWatched: draft.markWatched,
			trash: draft.trash,
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
			addTags: rule.addTags.join(', '),
			destination: rule.moveToPlaylistId ? 'move' : rule.copyToPlaylistId ? 'copy' : '',
			destinationId: rule.moveToPlaylistId ?? rule.copyToPlaylistId ?? '',
			markWatched: rule.markWatched,
			trash: rule.trash,
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

	const fieldClass = 'rounded-md border px-3 py-2 text-sm';
	const fieldStyle = 'border-color: var(--color-border); background: var(--color-bg)';
</script>

<svelte:head><title>Rules - linkbelli</title></svelte:head>

<section class="mx-auto max-w-3xl">
	<header class="flex items-start justify-between gap-4">
		<div>
			<h1 class="text-2xl font-semibold">Rules</h1>
			<p class="mt-1 text-sm" style="color: var(--color-muted)">
				What should happen to a link when it arrives. Rules run in order, top first, and only
				ever see what arrives after you write them.
			</p>
		</div>
		<button
			type="button"
			onclick={startNew}
			class="inline-flex shrink-0 items-center gap-2 rounded-md px-3 py-2 text-sm font-medium"
			style="background: var(--color-accent); color: var(--color-accent-contrast)"
		>
			<Plus size={16} aria-hidden="true" />
			New rule
		</button>
	</header>

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
							<Switch checked={rule.enabled} onchange={(value) => toggle(rule, value)} />
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
				<input bind:value={draft.name} placeholder="Long reads go to Later" class={fieldClass} style={fieldStyle} />
			</label>

			<fieldset class="mt-4 rounded-md border p-3" style="border-color: var(--color-border)">
				<legend class="px-1 text-xs" style="color: var(--color-muted)">When all of these are true</legend>
				<div class="grid gap-3 sm:grid-cols-2">
					<label class="flex flex-col gap-1 text-sm">
						<span>It lands in</span>
						<select bind:value={draft.playlistId} class={fieldClass} style={fieldStyle}>
							<option value="">Any playlist</option>
							{#each data.playlists as playlist (playlist.id)}
								<option value={playlist.id}>{playlist.name}</option>
							{/each}
						</select>
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>It is</span>
						<select bind:value={draft.kind} class={fieldClass} style={fieldStyle}>
							{#each kinds as option (option.value)}
								<option value={option.value}>{option.label}</option>
							{/each}
						</select>
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>From the site</span>
						<input bind:value={draft.host} placeholder="example.com" class={fieldClass} style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm">
						<span>Title matches</span>
						<input bind:value={draft.titlePattern} spellcheck="false" placeholder="rust|zig" class="{fieldClass} font-mono" style={fieldStyle} />
					</label>
					<label class="flex flex-col gap-1 text-sm sm:col-span-2">
						<span>Address matches</span>
						<input bind:value={draft.urlPattern} spellcheck="false" placeholder="/blog/" class="{fieldClass} font-mono" style={fieldStyle} />
					</label>
				</div>
				<p class="mt-2 text-xs" style="color: var(--color-muted)">
					Patterns are regular expressions and ignore case. Leave a box empty to skip that condition
					— a rule with none matches everything that arrives.
				</p>
			</fieldset>

			<fieldset class="mt-4 rounded-md border p-3" style="border-color: var(--color-border)">
				<legend class="px-1 text-xs" style="color: var(--color-muted)">Then</legend>
				<div class="grid gap-3 sm:grid-cols-2">
					<label class="flex flex-col gap-1 text-sm">
						<span>Tag it</span>
						<input bind:value={draft.addTags} placeholder="rust, later" class={fieldClass} style={fieldStyle} />
					</label>
					<div class="flex gap-2">
						<label class="flex flex-1 flex-col gap-1 text-sm">
							<span>And</span>
							<select bind:value={draft.destination} class={fieldClass} style={fieldStyle}>
								<option value="">Leave it where it is</option>
								<option value="move">Move it to</option>
								<option value="copy">Also put it in</option>
							</select>
						</label>
						{#if draft.destination}
							<label class="flex flex-1 flex-col gap-1 text-sm">
								<span class="sr-only">Destination playlist</span>
								<span aria-hidden="true">&nbsp;</span>
								<select bind:value={draft.destinationId} class={fieldClass} style={fieldStyle}>
									<option value="">Choose…</option>
									{#each data.playlists as playlist (playlist.id)}
										<option value={playlist.id}>{playlist.name}</option>
									{/each}
								</select>
							</label>
						{/if}
					</div>
				</div>

				<div class="mt-3 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
					<label class="flex items-center gap-2">
						<input type="checkbox" bind:checked={draft.markWatched} />
						Mark it watched
					</label>
					<label class="flex items-center gap-2">
						<input type="checkbox" bind:checked={draft.trash} />
						Send it to the trash
					</label>
					<label class="flex items-center gap-2">
						<input type="checkbox" bind:checked={draft.stopOnMatch} />
						Stop here
					</label>
				</div>
				<p class="mt-2 text-xs" style="color: var(--color-muted)">
					"Stop here" keeps the rules below from seeing the item — which is how a specific rule
					shields something from a broad one underneath it. Trashed items are recoverable.
				</p>
			</fieldset>

			<div class="mt-4 flex flex-wrap items-center gap-3">
				<button
					type="button"
					onclick={save}
					disabled={busy}
					class="rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
					style="background: var(--color-accent); color: var(--color-accent-contrast)"
				>{editing ? 'Save' : 'Create'}</button>

				<!-- A rule only ever acts on what arrives next, so this is the only way to find out
				     whether it works without waiting to see what it does. -->
				<button
					type="button"
					onclick={tryIt}
					disabled={busy}
					class="inline-flex items-center gap-2 rounded-md border px-3 py-2 text-sm disabled:opacity-60"
					style="border-color: var(--color-border)"
				>
					<Wand2 size={15} aria-hidden="true" />
					Try it on what I have
				</button>

				{#if error}
					<p class="text-sm" style="color: var(--color-danger)">{error}</p>
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
</section>
