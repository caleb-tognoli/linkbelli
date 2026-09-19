<svelte:head><title>Profile - linkbelli</title></svelte:head>

<script lang="ts">
	import { api } from '$lib/api/client';
	import ApiKeysManager from '$lib/components/ApiKeysManager.svelte';
	import BackupsPanel from '$lib/components/BackupsPanel.svelte';
	import NotificationsPanel from '$lib/components/NotificationsPanel.svelte';
	import WebhooksPanel from '$lib/components/WebhooksPanel.svelte';
	import OfflineSupportNotice from '$lib/components/OfflineSupportNotice.svelte';
	import ThemeToggle from '$lib/components/ThemeToggle.svelte';
	import Switch from '$lib/components/Switch.svelte';
	import { Bookmark, Download } from '@lucide/svelte';
	import { page } from '$app/state';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	/** Matches AccountDeletionService.GraceDays. */
	const GRACE_DAYS = 30;

	let password = $state('');
	let leaving = $state(false);
	let leaveError = $state<string | null>(null);
	let leavingAt = $state<string | null>(null);

	/**
	 * Asks for the account to go.
	 *
	 * Confirmed by password rather than by a dialog: a session left open on a shared machine
	 * should not be enough to end somebody's account, and "type DELETE to confirm" is a ritual
	 * that proves you can type.
	 */
	async function leave(event: SubmitEvent) {
		event.preventDefault();
		leaving = true;
		leaveError = null;

		const res = await api.del('/me', { password });
		leaving = false;
		password = '';

		if (res.ok) {
			leavingAt = ((await res.json()) as { deletesAt: string }).deletesAt;
		} else if (res.status === 400) {
			leaveError = 'That password is not right.';
		} else if (res.status === 429) {
			leaveError = 'Too many attempts. Wait a minute and try again.';
		} else {
			leaveError = 'Could not do that. Try again.';
		}
	}

	// Named by what the file is for, not by its extension — "OPML" means nothing until you know
	// it is the thing your feed reader imports.
	const EXPORTS = [
		{ format: 'json', label: 'Everything (JSON)', hint: 'Playlists, links, folders and sources' },
		{ format: 'csv', label: 'Links (CSV)', hint: 'One row per link — reads back into the importer' },
		{ format: 'html', label: 'Bookmarks (HTML)', hint: 'Import into any browser' },
		{ format: 'opml', label: 'Feeds (OPML)', hint: 'Your RSS sources, for a feed reader' }
	];

	let showNsfw = $state(data.user?.showNsfw ?? false);
	let archiveLinks = $state(data.user?.archiveLinks ?? false);

	// Both go in one request, so sending only the one that changed would reset the other.
	async function savePreferences() {
		await api.put('/me/preferences', { showNsfw, archiveLinks });
	}

	async function setNsfw(value: boolean) {
		showNsfw = value;
		await savePreferences();
	}

	async function setArchive(value: boolean) {
		archiveLinks = value;
		await savePreferences();
	}

	// Counts worth a number, and where a number that isn't zero is worth acting on.
	const usageEntries = $derived(
		data.usage
			? [
					{ label: 'Playlists', value: data.usage.playlists },
					{ label: 'Links', value: data.usage.items },
					{ label: 'Sites', value: data.usage.sites },
					{ label: 'Watched', value: data.usage.watched },
					{ label: 'Folders', value: data.usage.folders },
					{ label: 'Sources', value: data.usage.sources },
					{ label: 'Broken', value: data.usage.broken, href: '/search?broken=1', action: 'review' },
					{ label: 'In the trash', value: data.usage.inTrash, href: '/trash', action: 'review' }
				]
			: []
	);

	// Built against this origin so it works wherever the app is deployed, and kept to one line
	// because a bookmarklet is a URL, not a script file.
	const bookmarklet = $derived(
		"javascript:(function(){window.open(" +
			`'${page.url.origin}/save?url='+encodeURIComponent(location.href)+'&title='+encodeURIComponent(document.title)` +
			",'_blank','noopener,width=460,height=560');})();"
	);

	function pct(used: number, max: number) {
		return max > 0 ? Math.min(100, Math.round((used / max) * 100)) : 0;
	}
</script>

<section class="mx-auto flex max-w-4xl flex-col gap-10">
	<h1 class="text-2xl font-semibold">Profile</h1>

	<div>
		<h2 class="font-medium">Theme</h2>
		<div class="mt-3">
			<ThemeToggle initial={data.theme} />
		</div>
	</div>

	<div>
		<h2 class="font-medium">Content</h2>
		<label class="mt-3 flex items-center gap-2 text-sm">
			<Switch checked={showNsfw} onchange={setNsfw} label="Show adult content" />
			Show NSFW
		</label>
	</div>

	<div>
		<h2 class="font-medium">Archiving</h2>
		<label class="mt-3 flex items-center gap-2 text-sm">
			<Switch checked={archiveLinks} onchange={setArchive} label="Archive links to the Wayback Machine" />
			Keep a public snapshot of pages I save
		</label>
		<!-- Off by default, and the reason is worth saying out loud rather than burying: this
		     sends addresses to someone else. -->
		<p class="mt-2 max-w-prose text-sm" style="color: var(--color-muted)">
			Asks the Internet Archive for a copy, so a link still leads somewhere once the original
			is gone. It means sending the addresses you save to a third party, and the snapshots are
			public — which is why this is off unless you turn it on.
		</p>
	</div>

	<div>
		<h2 class="font-medium">Profile</h2>
		{#if data.user}
			<!-- minmax(0, 1fr) and wrapping anywhere: an email address is one unbreakable word, and
			     a plain 1fr column refuses to be narrower than it — which made the whole page scroll
			     sideways on a phone. -->
			<dl
				class="mt-3 grid grid-cols-[8rem_minmax(0,1fr)] gap-y-2 rounded-lg border p-4 text-sm"
				style="border-color: var(--color-border); background: var(--color-surface)"
			>
				<dt style="color: var(--color-muted)">Username</dt>
				<dd class="[overflow-wrap:anywhere]">{data.user.username ?? '—'}</dd>
				<dt style="color: var(--color-muted)">Email</dt>
				<dd class="[overflow-wrap:anywhere]">{data.user.email ?? '—'}</dd>
			</dl>
		{/if}
	</div>

	{#if data.usage}
		<div>
			<h2 class="font-medium">What you have here</h2>
			<dl
				class="mt-3 grid grid-cols-2 gap-x-6 gap-y-3 rounded-lg border p-4 text-sm sm:grid-cols-4"
				style="border-color: var(--color-border); background: var(--color-surface)"
			>
				{#each usageEntries as entry (entry.label)}
					<div>
						<dt style="color: var(--color-muted)">{entry.label}</dt>
						<dd class="text-lg font-semibold tabular-nums">
							{entry.value}
							{#if entry.href && entry.value > 0}
								<a href={entry.href} class="ml-1 text-xs font-normal underline underline-offset-2" style="color: var(--color-accent)">
									{entry.action}
								</a>
							{/if}
						</dd>
					</div>
				{/each}
			</dl>
		</div>
	{/if}

	{#if data.quota}
		<div>
			<h2 class="font-medium">Quota</h2>
			<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
				{#each [
					{ label: 'Sources', used: data.quota.sourcesUsed, max: data.quota.maxSources },
					{ label: 'Runs today', used: data.quota.runsUsedToday, max: data.quota.maxRunsPerDay },
					{ label: 'Items / run', used: 0, max: data.quota.maxItemsPerRun }
				] as q (q.label)}
					<div
						class="rounded-lg border p-3"
						style="border-color: var(--color-border); background: var(--color-surface)"
					>
						<div class="text-sm" style="color: var(--color-muted)">{q.label}</div>
						<div class="mt-1 text-lg font-semibold">
							{#if q.label === 'Items / run'}{q.max}{:else}{q.used} / {q.max}{/if}
						</div>
						{#if q.label !== 'Items / run'}
							<div class="mt-2 h-1.5 overflow-hidden rounded-full" style="background: var(--color-border)">
								<div
									class="h-full"
									style="width: {pct(q.used, q.max)}%; background: var(--color-accent)"
								></div>
							</div>
						{/if}
					</div>
				{/each}
			</div>
		</div>
	{/if}

	<div>
		<h2 class="font-medium">Save from anywhere</h2>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Drag this to your bookmarks bar. Clicking it on any page opens Linkbelli with the address
			already filled in — no extension needed.
		</p>
		<p class="mt-3">
			<!-- A javascript: href is exactly what a bookmarklet is; it never runs from this page. -->
			<a
				href={bookmarklet}
				onclick={(e) => e.preventDefault()}
				class="inline-flex cursor-grab items-center gap-1.5 rounded-md border px-3 py-2 text-sm font-medium"
				style="border-color: var(--color-accent); color: var(--color-accent)"
				title="Drag me to your bookmarks bar"
			>
				<Bookmark size={15} aria-hidden="true" /> Save to Linkbelli
			</a>
		</p>
		<p class="mt-2 text-xs" style="color: var(--color-muted)">
			On a phone, install Linkbelli to your home screen and it shows up in the system share sheet.
		</p>
		<div class="mt-2">
			<OfflineSupportNotice compact detail />
		</div>
	</div>

	<div>
		<h2 class="font-medium">Export</h2>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Download everything you have here. It is your data; take it wherever you like.
		</p>
		<div class="mt-3 flex flex-wrap gap-2">
			{#each EXPORTS as fmt (fmt.format)}
				<a
					href={`/api/v1/export?format=${fmt.format}`}
					download
					class="inline-flex items-center gap-1.5 rounded-md border px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
					style="border-color: var(--color-border)"
					title={fmt.hint}
				>
					<Download size={15} aria-hidden="true" />
					{fmt.label}
				</a>
			{/each}
		</div>
	</div>

	<NotificationsPanel email={data.user?.email} confirmed={data.user?.emailConfirmed ?? true} />

	<BackupsPanel enabled={data.user?.backupsEnabled ?? true} />

	<div>
		<ApiKeysManager keys={data.apiKeys} />
	</div>

	<WebhooksPanel />

	<!-- The counterpart of the four export formats above. Data portability was taken seriously
	     here and its opposite was missing entirely: there was no way out at all. -->
	<div class="border-t pt-8" style="border-color: var(--color-border)">
		<h2 class="font-medium" style="color: var(--color-danger)">Close this account</h2>

		{#if leavingAt}
			<p class="mt-1 max-w-prose text-sm">
				Scheduled for {new Date(leavingAt).toLocaleDateString(undefined, {
					day: 'numeric',
					month: 'long',
					year: 'numeric'
				})}. Everything you published is already hidden and your sources have stopped.
			</p>
			<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
				Sign in again before then and it is called off — nothing is lost, and what you had
				published goes back up exactly as it was.
			</p>
		{:else}
			<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
				Your playlists, links, sources, folders, keys and backups all go, after
				{GRACE_DAYS} days. Take an export first — once it runs there is nothing to come back to.
			</p>
			<p class="mt-1 max-w-prose text-sm" style="color: var(--color-muted)">
				Links you saved that other people also saved stay, because they are the same rows;
				so does any copy somebody took of a playlist you published, which is theirs now.
			</p>

			<form class="mt-3 flex flex-wrap items-end gap-2" onsubmit={leave}>
				<label class="flex flex-col gap-1 text-sm">
					Confirm with your password
					<input
						type="password"
						bind:value={password}
						autocomplete="current-password"
						class="rounded-md border px-3 py-2"
						style="border-color: var(--color-border-strong); background: var(--color-bg)"
					/>
				</label>
				<button
					type="submit"
					disabled={leaving || !password}
					class="rounded-md border px-3 py-2 text-sm font-medium disabled:opacity-60"
					style="border-color: var(--color-danger); color: var(--color-danger)"
				>
					{leaving ? 'Closing…' : 'Close my account'}
				</button>
			</form>

			{#if leaveError}
				<p class="mt-2 text-sm" style="color: var(--color-danger)" role="alert">{leaveError}</p>
			{/if}
		{/if}
	</div>
</section>
