<script lang="ts">
	import { api, json } from '$lib/api/client';
	import Switch from '$lib/components/Switch.svelte';
	import { describeContents, formatAge, formatSize, type Backup } from '$lib/backups';
	import { Download, Trash2 } from '@lucide/svelte';

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
	<!-- Said plainly rather than left to be discovered on the day it matters: there is no restore
	     yet, and somebody betting their library on a button that does not exist is worse off than
	     somebody who knew to keep their own copy. -->
	<p class="mt-2 max-w-prose text-sm" style="color: var(--color-muted)">
		Downloading one gives you the same file as <a
			href="/api/v1/export?format=json"
			download
			class="underline underline-offset-2">Export</a
		>. There is no one-click restore yet — putting a backup back means importing its links,
		which recovers what you saved but not how it was arranged.
	</p>

	<label class="mt-3 flex items-center gap-2 text-sm">
		<Switch checked={enabled} onchange={setEnabled} />
		Keep weekly backups
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
