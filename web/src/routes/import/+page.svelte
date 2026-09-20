<svelte:head><title>{pageTitle('Import')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import Select from '$lib/components/ui/Select.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
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
		<div class="rounded-card border p-6" style="border-color: var(--color-border); background: var(--color-surface)">
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
				<div class="mb-4 rounded-control border p-3 text-sm" style="border-color: var(--color-border)">
					<p class="mb-1.5 font-medium" style="color: var(--color-danger)">Failed rows</p>
					<ul class="space-y-1" style="color: var(--color-muted)">
						{#each form.result.errors as err, index (index)}
							<li class="truncate">{err}</li>
						{/each}
					</ul>
				</div>
			{/if}

			<Button href="/import" icon={Upload}>Import another file</Button>
		</div>
	{:else}
		<!-- Import form -->
		<form
			method="post"
			action="?/import"
			enctype="multipart/form-data"
			use:enhance={() => {
				submitting = true;
				// Cleared after the result is on the page, not before it: the other way round
				// dropped the spinner while the import was still being applied, so the button went
				// idle in front of a page that had not changed yet.
				return async ({ update }) => {
					await update();
					submitting = false;
				};
			}}
			class="space-y-7"
		>
			{#if form && 'error' in form && form.error}
				<p
					class="rounded-control border px-3 py-2 text-sm"
					style="border-color: var(--color-danger); color: var(--color-danger)"
					role="alert"
				>
					{form.error}
				</p>
			{/if}

			<Field
				label="File"
				required
				hint="A browser's bookmark export (.html), a plain list of addresses, one per line, or a CSV with a url column and an optional note."
			>
				{#snippet children(f)}
					<input
						id={f.id}
						name="file"
						type="file"
						accept=".csv,.html,.htm,.txt,text/csv,text/html,text/plain"
						required
						aria-describedby={f.describedby}
						class="block w-full rounded-control border border-border-strong bg-bg px-3 py-2 text-sm file:mr-3 file:cursor-pointer file:rounded-control file:border-0 file:px-3 file:py-1 file:text-sm file:font-medium"
					/>
				{/snippet}
			</Field>
			<p class="-mt-5 text-xs text-muted">
				<a href="/import/sample.csv" download class="text-accent underline underline-offset-2">
					Download a sample CSV
				</a>
			</p>

			<!-- A fieldset, so the choice and the field it reveals are one thing rather than a
			     paragraph that happens to sit above them. -->
			<fieldset class="space-y-3">
				<legend class="text-sm font-medium">Add to</legend>

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
							<Field label="Playlist to import into" hideLabel>
								{#snippet children(f)}
									<Select id={f.id} name="playlistId" aria-describedby={f.describedby}>
										{#each data.playlists as pl (pl.id)}
											<option value={pl.id}>{pl.name}</option>
										{/each}
									</Select>
								{/snippet}
							</Field>
						{/if}
					</div>
				{/if}

				{#if destination === 'new'}
					<!-- A label rather than a placeholder, which goes the moment anybody types into
					     it — and required, since choosing "New playlist" and leaving it empty was
					     something only the server found out about. -->
					<Field label="Name of the new playlist" required>
						{#snippet children(f)}
							<Input
								id={f.id}
								name="newPlaylistName"
								type="text"
								placeholder="Reading queue"
								required
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
				{/if}
			</fieldset>

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
