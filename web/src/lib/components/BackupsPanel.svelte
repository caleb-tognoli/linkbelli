<script lang="ts">
	import Button from '$lib/components/ui/Button.svelte';
	import { invalidateAll } from '$app/navigation';
	import { api, json } from '$lib/api/client';
	import Switch from '$lib/components/Switch.svelte';
	import {
		describeContents,
		formatAge,
		formatSize,
		type Backup,
		type RestorePlan
	} from '$lib/backups';
	import { Download, RotateCcw, Trash2, Upload, X } from '@lucide/svelte';

	let { enabled: initialEnabled = true }: { enabled?: boolean } = $props();

	let enabled = $state(initialEnabled);
	let backups = $state<Backup[]>([]);
	let loading = $state(true);
	let working = $state(false);
	let error = $state<string | null>(null);

	// Said after a "Back up now" that found nothing to do — otherwise the button looks broken
	// when it is in fact reporting that the last snapshot is still current.
	let unchanged = $state(false);

	async function refresh() {
		try {
			const res = await api.get('/backups');
			backups = await json<Backup[]>(res);
			error = null;
		} catch {
			error = 'Could not load your backups.';
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		refresh();
	});

	async function setEnabled(value: boolean) {
		const previous = enabled;
		enabled = value;

		// Only the setting this panel owns. Every field of the request is optional and an omitted
		// one is left alone, so this cannot reach across and change what another screen set.
		const res = await api.put('/me/preferences', { backupsEnabled: value });
		if (!res.ok) {
			// Put the switch back rather than leaving it showing a setting the server never took.
			enabled = previous;
			error = 'Could not save that setting.';
		}
	}

	async function backUpNow() {
		working = true;
		unchanged = false;
		try {
			const res = await api.post('/backups', {});
			if (!res.ok) throw new Error(String(res.status));

			// 204 means nothing has changed since the last one, so there was nothing to write.
			unchanged = res.status === 204;
			if (!unchanged) await refresh();
			error = null;
		} catch {
			error = 'Could not take a backup just now.';
		} finally {
			working = false;
		}
	}

	// Under the server's 30 MB request ceiling with room for JSON string-escaping on the way.
	const MAX_FILE_BYTES = 20 * 1024 * 1024;

	let plan = $state<RestorePlan | null>(null);

	// What the shown plan was a plan for: a stored snapshot, or a file somebody picked off their
	// own disk. Held so "Go ahead" restores the thing that was just costed, and not something else.
	let pending = $state<{ id: string } | { file: string } | null>(null);
	let picker = $state<HTMLInputElement | null>(null);

	function count(n: number, one: string): string {
		return `${n} ${n === 1 ? one : one + 's'}`;
	}

	/**
	 * Asks what a restore would do, without doing it.
	 *
	 * Two steps rather than one confirmation dialog, because "are you sure" tells somebody
	 * nothing and "this would add 412 links across 6 playlists, and leave 900 alone" tells them
	 * everything they need to decide.
	 */
	async function preview(source: { id: string } | { file: string }) {
		working = true;
		error = null;
		plan = null;

		try {
			plan =
				'id' in source
					? await json<RestorePlan>(await api.get(`/backups/${source.id}/restore`))
					: await json<RestorePlan>(
							await api.post('/backups/restore', { json: source.file, dryRun: true })
						);
			pending = source;
		} catch {
			error =
				'id' in source
					? 'Could not read that backup.'
					: 'That file is not a Linkbelli export, or it was written by a newer version.';
			pending = null;
		} finally {
			working = false;
		}
	}

	async function confirmRestore() {
		if (!pending) return;

		working = true;
		error = null;

		const res =
			'id' in pending
				? await api.post(`/backups/${pending.id}/restore`)
				: await api.post('/backups/restore', { json: pending.file });

		working = false;
		pending = null;

		if (res.ok) {
			plan = (await res.json()) as RestorePlan;
			// Playlist counts, folder tree and sidebar are all a snapshot older than the truth now.
			await invalidateAll();
		} else {
			plan = null;
			error = 'Could not restore that. Nothing was changed.';
		}
	}

	/**
	 * Reading the file here rather than posting it as a multipart upload: an export is JSON the
	 * restore endpoint already takes as a string, and this keeps one code path for both sources.
	 */
	async function pickFile(event: Event) {
		const input = event.currentTarget as HTMLInputElement;
		const file = input.files?.[0];

		// Chosen, then chosen again — clearing lets the same file be picked twice in a row.
		input.value = '';
		if (!file) return;

		// The server stops accepting a request body at 30 MB. Saying so here beats letting the
		// upload run its course and come back as an unexplained failure.
		if (file.size > MAX_FILE_BYTES) {
			error = `That file is ${formatSize(file.size)}, which is too big to send in one piece.`;
			return;
		}

		await preview({ file: await file.text() });
	}

	async function remove(backup: Backup) {
		if (!confirm(`Delete the backup from ${formatAge(backup.takenAt)}? This cannot be undone.`)) {
			return;
		}

		const res = await api.del(`/backups/${backup.id}`);
		if (res.ok) {
			backups = backups.filter((b) => b.id !== backup.id);
			error = null;
		} else {
			error = 'Could not delete that backup.';
		}
	}
</script>

<div>
	<h2 class="font-medium">Backups</h2>
	<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
		A copy of everything you have here, taken weekly and kept for the last five. Nothing leaves
		this server, and an unchanged library is not copied again.
	</p>
	<p class="mt-2 max-w-prose text-sm" style="color: var(--color-muted)">
		Downloading one gives you the same file as <a
			href="/api/v1/export?format=json"
			download
			class="underline underline-offset-2">Export</a
		>. Restoring adds back what is missing and leaves alone what is already here — so anything
		you have saved since a snapshot survives putting that snapshot back.
	</p>

	{#if plan}
		<!-- The numbers before the button, because the only way to trust a restore is to be told
		     what it will do while it is still possible to decide otherwise. -->
		<div class="mt-3 rounded-md border p-3 text-sm" style="border-color: var(--color-accent)">
			<p class="font-medium">
				{plan.dryRun ? 'Restoring' : 'Restored'} the snapshot from {formatAge(plan.takenAt)}
			</p>
			<ul class="mt-1 list-inside list-disc" style="color: var(--color-muted)">
				<li>
					{count(plan.playlistsAdded, 'playlist')}
					{plan.dryRun ? 'to add' : 'added'}, {plan.playlistsMatched} already here
				</li>
				<li>
					{count(plan.itemsAdded, 'link')}
					{plan.dryRun ? 'to put back' : 'put back'}, {plan.itemsAlreadyThere} already saved
				</li>
				{#if plan.foldersAdded}
					<li>{count(plan.foldersAdded, 'folder')} {plan.dryRun ? 'to rebuild' : 'rebuilt'}</li>
				{/if}
				{#if plan.highlightsAdded}
					<li>
						{count(plan.highlightsAdded, 'highlight')}
						{plan.dryRun ? 'to put back' : 'put back'}
					</li>
				{/if}
				{#if plan.sourcesAdded}
					<li>
						{count(plan.sourcesAdded, 'source')}, paused{plan.sourcesNeedCredentials
							? ' — their passwords and keys were kept out of the backup on purpose, so some will need typing in again'
							: ''}
					</li>
				{/if}
				{#if plan.truncated}
					<li>
						More than can be restored in one go. Run it again to carry on where this left off.
					</li>
				{/if}
			</ul>

			{#if plan.dryRun}
				<div class="mt-2 flex flex-wrap gap-2">
					<Button variant="primary" size="sm" icon={RotateCcw} onclick={confirmRestore} loading={working}>
						{working ? 'Restoring…' : 'Restore it'}
					</Button>
					<Button
						size="sm"
						icon={X}
						onclick={() => {
							plan = null;
							pending = null;
						}}
					>
						Cancel
					</Button>
				</div>
			{/if}
		</div>
	{/if}

	<label class="mt-3 flex items-center gap-2 text-sm">
		<Switch checked={enabled} onchange={setEnabled} labelledby="backups-weekly" />
		<span id="backups-weekly">Keep weekly backups</span>
	</label>

	<div class="mt-4 flex flex-wrap items-center gap-3">
		<button
			type="button"
			onclick={backUpNow}
			disabled={working}
			class="rounded-md border px-3 py-2 text-sm hover:bg-black/5 disabled:opacity-50 dark:hover:bg-white/10"
			style="border-color: var(--color-border)"
		>
			{working ? 'Backing up…' : 'Back up now'}
		</button>
		<!-- The way back for somebody whose account is gone: they still have the file they
		     downloaded, and nothing here is keyed to the server that wrote it. -->
		<button
			type="button"
			onclick={() => picker?.click()}
			disabled={working}
			class="flex items-center gap-2 rounded-md border px-3 py-2 text-sm hover:bg-black/5 disabled:opacity-50 dark:hover:bg-white/10"
			style="border-color: var(--color-border)"
		>
			<Upload size={15} aria-hidden="true" />
			Restore from a file
		</button>
		<input
			bind:this={picker}
			type="file"
			accept="application/json,.json"
			onchange={pickFile}
			class="hidden"
		/>
		{#if unchanged}
			<span class="text-sm" style="color: var(--color-muted)">
				Nothing has changed since the last one.
			</span>
		{/if}
	</div>

	{#if error}
		<p class="mt-3 text-sm" style="color: var(--color-danger)">{error}</p>
	{/if}

	{#if loading}
		<p class="mt-3 text-sm" style="color: var(--color-muted)">Looking…</p>
	{:else if backups.length === 0}
		<p class="mt-3 text-sm" style="color: var(--color-muted)">
			{enabled
				? 'No backups yet — the first one is taken within the hour.'
				: 'No backups, and none will be taken while this is off.'}
		</p>
	{:else}
		<ul
			class="mt-3 divide-y rounded-lg border text-sm"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			{#each backups as backup (backup.id)}
				<li class="flex items-center gap-3 p-3" style="border-color: var(--color-border)">
					<div class="min-w-0 flex-1">
						<div class="font-medium">
							{formatAge(backup.takenAt)}
							{#if !backup.automatic}
								<span class="ml-1 text-xs font-normal" style="color: var(--color-muted)">
									· taken by you
								</span>
							{/if}
						</div>
						<div class="text-xs" style="color: var(--color-muted)">
							{describeContents(backup)} · {formatSize(backup.sizeBytes)}
						</div>
					</div>
					<button
						type="button"
						onclick={() => preview({ id: backup.id })}
						disabled={working}
						title="See what restoring this would do"
						class="rounded-md border p-2 hover:bg-black/5 disabled:opacity-60 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						<RotateCcw size={15} aria-hidden="true" />
						<span class="sr-only">Restore</span>
					</button>
					<a
						href={`/api/v1/backups/${backup.id}`}
						download
						title="Download this backup"
						class="rounded-md border p-2 hover:bg-black/5 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						<Download size={15} aria-hidden="true" />
						<span class="sr-only">Download</span>
					</a>
					<button
						type="button"
						onclick={() => remove(backup)}
						title="Delete this backup"
						class="rounded-md border p-2 hover:bg-black/5 dark:hover:bg-white/10"
						style="border-color: var(--color-border)"
					>
						<Trash2 size={15} aria-hidden="true" />
						<span class="sr-only">Delete</span>
					</button>
				</li>
			{/each}
		</ul>
	{/if}
</div>
