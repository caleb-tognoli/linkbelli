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
	import { Upload, CheckCircle, FileText, X } from '@lucide/svelte';
	import { enhance } from '$app/forms';
	import { parseRows } from '$lib/importFormats';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let destination = $state('none');
	let submitting = $state(false);

	/**
	 * The file, once there is one, described before it is sent.
	 *
	 * The whole page is one file, and it was a bare file input: no way to drop anything on it, and
	 * nothing after choosing but the browser's own filename. Somebody with a four thousand row
	 * bookmark export pressed Import with no idea what was in it. The same parsers the server
	 * uses run here first, so the count is the real one.
	 */
	let picker = $state<HTMLInputElement>();
	let dragging = $state(false);
	let chosen = $state<{ name: string; size: number; found: number | null } | null>(null);
	let reading = $state(false);

	const KB = 1024;
	function fileSize(bytes: number): string {
		if (bytes < KB) return `${bytes} bytes`;
		if (bytes < KB * KB) return `${Math.round(bytes / KB)} KB`;
		return `${(bytes / (KB * KB)).toFixed(1)} MB`;
	}

	async function describe(file: File | undefined) {
		if (!file) {
			chosen = null;
			return;
		}

		chosen = { name: file.name, size: file.size, found: null };
		reading = true;
		try {
			// Only worth reading here while it is small enough to be instant; past that the
			// name and the size are enough to know the right file was picked.
			if (file.size <= 4 * KB * KB) {
				chosen = { ...chosen, found: parseRows(await file.text(), file.name).length };
			}
		} catch {
			// An unreadable file is the server's to complain about, with a better message.
		} finally {
			reading = false;
		}
	}

	/** "· 94 KB · 1,204 addresses", or just the size while it is still being counted. */
	function detail(file: { size: number; found: number | null }): string {
		const parts = [fileSize(file.size)];
		if (reading) parts.push('reading…');
		else if (file.found !== null) {
			parts.push(`${file.found.toLocaleString()} ${file.found === 1 ? 'address' : 'addresses'}`);
		}
		return `· ${parts.join(' · ')}`;
	}

	/** Puts a dropped file into the real input, which is what the form submits. */
	function onDrop(event: DragEvent) {
		dragging = false;
		const file = event.dataTransfer?.files?.[0];
		if (!file || !picker) return;

		const box = new DataTransfer();
		box.items.add(file);
		picker.files = box.files;
		void describe(file);
	}

	function clearFile() {
		if (picker) picker.value = '';
		chosen = null;
		picker?.focus();
	}
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
					<!-- svelte-ignore a11y_no_static_element_interactions, a11y_no_noninteractive_element_interactions -->
					<label
						for={f.id}
						class="flex cursor-pointer flex-col items-center gap-2 rounded-card border border-dashed px-6 py-8 text-center transition-colors {dragging
							? 'border-accent bg-selected'
							: 'border-border-strong'}"
						ondragover={(e) => {
							e.preventDefault();
							dragging = true;
						}}
						ondragleave={() => (dragging = false)}
						ondrop={(e) => {
							e.preventDefault();
							onDrop(e);
						}}
					>
						<Upload size={20} aria-hidden="true" class="text-muted" />
						<span class="text-sm">
							<span class="font-medium text-accent">Choose a file</span>
							<span class="text-muted"> or drop one here</span>
						</span>
						<input
							bind:this={picker}
							id={f.id}
							name="file"
							type="file"
							accept=".csv,.html,.htm,.txt,text/csv,text/html,text/plain"
							required
							aria-describedby={f.describedby}
							class="sr-only"
							onchange={(e) => describe(e.currentTarget.files?.[0])}
						/>
					</label>

					{#if chosen}
						<!-- What is about to be sent, said before it is: the name, how big it is, and
						     how many addresses are actually in it. -->
						<p class="mt-2 flex items-center gap-2 text-sm" role="status">
							<FileText size={15} aria-hidden="true" class="shrink-0 text-muted" />
							<span class="min-w-0 flex-1 truncate">
								<span class="font-medium">{chosen.name}</span>
								<!-- One string, so the separators cannot lose their spaces to the
								     template's own whitespace. -->
								<span class="text-muted">{detail(chosen)}</span>
							</span>
							<Button
								variant="ghost"
								size="sm"
								icon={X}
								iconOnly
								label="Choose a different file"
								onclick={clearFile}
							/>
						</p>
					{/if}
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
