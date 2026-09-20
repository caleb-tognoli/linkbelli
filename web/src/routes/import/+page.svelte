<svelte:head><title>Import - linkbelli</title></svelte:head>

<script lang="ts">
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { Upload, CheckCircle } from '@lucide/svelte';
	import { enhance } from '$app/forms';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let destination = $state('none');
	let submitting = $state(false);
</script>

<Page width="form">
	<PageHeader title="Import links" class="mb-6" />

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
						{#each form.result.errors as err, index (index)}
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
				<p
					class="rounded-md border px-3 py-2 text-sm"
					style="border-color: var(--color-danger); color: var(--color-danger)"
					role="alert"
				>
					{form.error}
				</p>
			{/if}

			<!-- File upload -->
			<div class="space-y-1.5">
				<label for="file" class="block text-sm font-medium">File</label>
				<input
					id="file"
					name="file"
					type="file"
					accept=".csv,.html,.htm,.txt,text/csv,text/html,text/plain"
					required
					aria-describedby="file-hint"
					class="block w-full rounded-control border border-border-strong bg-bg px-3 py-2 text-sm file:mr-3 file:cursor-pointer file:rounded file:border-0 file:px-3 file:py-1 file:text-sm file:font-medium"
				/>
				<p id="file-hint" class="text-xs text-muted">
					A browser's bookmark export (<code>.html</code>), a plain list of addresses, one per
					line, or a CSV with a <code>url</code> column and an optional <code>note</code>.
					<a href="/import/sample.csv" download class="underline underline-offset-2 text-accent">
						Download a sample CSV
					</a>.
				</p>
			</div>

			<!-- Destination -->
			<div class="space-y-3">
				<p class="text-sm font-medium">Add to</p>

				<input type="hidden" name="destination" value={destination} />
				<SegmentedControl
					label="Add to"
					options={[
						{ value: 'none', label: 'No playlist' },
						{ value: 'existing', label: 'Existing playlist' },
						{ value: 'new', label: 'New playlist' }
					]}
					bind:value={destination}
				/>
				{#if destination === 'none'}
					<!-- "None" read as "do nothing with these". They are saved either way; the choice
					     is only whether they land in a list as well. -->
					<p class="text-xs text-muted">
						The links are saved to your library and searchable, but not put in a playlist.
					</p>
				{/if}

				{#if destination === 'existing'}
					<div>
						{#if data.playlists.length === 0}
							<p class="text-sm" style="color: var(--color-muted)">You don't have any playlists yet.</p>
						{:else}
							<Select name="playlistId" aria-label="Playlist to import into">
								{#each data.playlists as pl (pl.id)}
									<option value={pl.id}>{pl.name}</option>
								{/each}
							</Select>
						{/if}
					</div>
				{/if}

				{#if destination === 'new'}
					<div>
						<Input
							name="newPlaylistName"
							type="text"
							placeholder="Playlist name"
							aria-label="Name of the new playlist"
						/>
					</div>
				{/if}
			</div>

			<Button
				type="submit"
				variant="primary"
				icon={Upload}
				loading={submitting}
				disabled={destination === 'existing' && data.playlists.length === 0}
			>
				{submitting ? 'Importing…' : 'Import'}
			</Button>
		</form>
	{/if}
</Page>
