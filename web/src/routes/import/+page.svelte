<svelte:head><title>Import - linkbelli</title></svelte:head>

<script lang="ts">
	import { Upload, CheckCircle } from '@lucide/svelte';
	import { enhance } from '$app/forms';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let destination = $state('none');
	let submitting = $state(false);
</script>

<div class="max-w-4xl">
	<h1 class="mb-8 text-2xl font-semibold">Import links</h1>

	{#if form && 'success' in form && form.success}
		<!-- Result display -->
		<div class="rounded-lg border p-6" style="border-color: var(--color-border); background: var(--color-surface)">
			<div class="mb-4 flex items-center gap-2.5">
				<CheckCircle size={20} aria-hidden="true" style="color: var(--color-accent)" />
				<span class="font-semibold">Import complete</span>
			</div>

			<div class="mb-4 flex gap-8 text-sm">
				<div>
					<span class="text-2xl font-bold" style="color: var(--color-accent)">{form.result.imported}</span>
					<span class="ml-1" style="color: var(--color-muted)">imported</span>
				</div>
				{#if form.result.skipped > 0}
					<div>
						<span class="text-2xl font-bold">{form.result.skipped}</span>
						<span class="ml-1" style="color: var(--color-muted)">skipped</span>
					</div>
				{/if}
				{#if form.result.errors.length > 0}
					<div>
						<span class="text-2xl font-bold" style="color: var(--color-danger)">{form.result.errors.length}</span>
						<span class="ml-1" style="color: var(--color-muted)">errors</span>
					</div>
				{/if}
			</div>

			{#if form.result.errors.length > 0}
				<div class="mb-4 rounded-md border p-3 text-sm" style="border-color: var(--color-border)">
					<p class="mb-1.5 font-medium" style="color: var(--color-danger)">Failed rows</p>
					<ul class="space-y-1" style="color: var(--color-muted)">
						{#each form.result.errors as err}
							<li class="truncate">{err}</li>
						{/each}
					</ul>
				</div>
			{/if}

			<a
				href="/import"
				class="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm hover:bg-black/5 dark:hover:bg-white/10"
				style="border-color: var(--color-border)"
			>
				<Upload size={15} aria-hidden="true" />
				Import another file
			</a>
		</div>
	{:else}
		<!-- Import form -->
		<form
			method="post"
			action="?/import"
			enctype="multipart/form-data"
			use:enhance={() => {
				submitting = true;
				return async ({ update }) => {
					submitting = false;
					await update();
				};
			}}
			class="space-y-7"
		>
			{#if form && 'error' in form && form.error}
				<p class="rounded-md border px-3 py-2 text-sm" style="border-color: var(--color-danger); color: var(--color-danger)">
					{form.error}
				</p>
			{/if}

			<!-- File upload -->
			<div class="space-y-1.5">
				<label for="file" class="block text-sm font-medium">CSV file</label>
				<input
					id="file"
					name="file"
					type="file"
					accept=".csv,text/csv"
					required
					class="block w-full rounded-md border px-3 py-2 text-sm file:mr-3 file:cursor-pointer file:rounded file:border-0 file:px-3 file:py-1 file:text-sm file:font-medium"
					style="border-color: var(--color-border); background: var(--color-bg)"
				/>
				<p class="text-xs" style="color: var(--color-muted)">
					Expected format: <code>url,note</code> — one link per row, header row required.
				</p>
			</div>

			<!-- Destination -->
			<div class="space-y-3">
				<p class="text-sm font-medium">Add to</p>

				<input type="hidden" name="destination" value={destination} />
				<div class="inline-flex overflow-hidden rounded-md border text-sm" style="border-color: var(--color-border)">
					{#each [{ value: 'none', label: 'None' }, { value: 'existing', label: 'Existing playlist' }, { value: 'new', label: 'New playlist' }] as opt (opt.value)}
						<button
							type="button"
							onclick={() => (destination = opt.value)}
							class="px-3 py-1.5"
							style={destination === opt.value
								? 'background: var(--color-accent); color: var(--color-accent-contrast)'
								: 'color: var(--color-muted)'}
							aria-pressed={destination === opt.value}
						>{opt.label}</button>
					{/each}
				</div>

				{#if destination === 'existing'}
					<div>
						{#if data.playlists.length === 0}
							<p class="text-sm" style="color: var(--color-muted)">You don't have any playlists yet.</p>
						{:else}
							<select
								name="playlistId"
								class="rounded-md border px-3 py-2 text-sm"
								style="border-color: var(--color-border); background: var(--color-bg)"
							>
								{#each data.playlists as pl}
									<option value={pl.id}>{pl.name}</option>
								{/each}
							</select>
						{/if}
					</div>
				{/if}

				{#if destination === 'new'}
					<div>
						<input
							name="newPlaylistName"
							type="text"
							placeholder="Playlist name"
							class="rounded-md border px-3 py-2 text-sm"
							style="border-color: var(--color-border); background: var(--color-bg)"
						/>
					</div>
				{/if}
			</div>

			<button
				type="submit"
				disabled={submitting || (destination === 'existing' && data.playlists.length === 0)}
				class="inline-flex items-center gap-1.5 rounded-md px-4 py-2 text-sm font-medium disabled:opacity-60"
				style="background: var(--color-accent); color: var(--color-accent-contrast)"
			>
				<Upload size={16} aria-hidden="true" />
				{submitting ? 'Importing…' : 'Import'}
			</button>
		</form>
	{/if}
</div>
