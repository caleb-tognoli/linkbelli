<svelte:head><title>{pageTitle('Settings')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import PageHeader from '$lib/components/ui/PageHeader.svelte';
	import Page from '$lib/components/ui/Page.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { api } from '$lib/api/client';
	import ApiKeysManager from '$lib/components/ApiKeysManager.svelte';
	import ChangePasswordForm from '$lib/components/ChangePasswordForm.svelte';
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

	/**
	 * The page in groups, by what somebody comes here to do.
	 *
	 * It was thirteen sections in one column under a heading that said "Profile" twice, with
	 * closing the account somewhere down near the webhooks. Five groups now, each one click from
	 * the list beside them, and closing the account on its own at the end.
	 */
	const SECTIONS = [
		{ id: 'account', label: 'Account' },
		{ id: 'appearance', label: 'Appearance & content' },
		{ id: 'email', label: 'Email' },
		{ id: 'data', label: 'Your data' },
		{ id: 'integrations', label: 'Integrations' },
		{ id: 'close', label: 'Close account' }
	] as const;

	let current = $state<string>(SECTIONS[0].id);

	// Follows the reading position, so the list says where on the page you are.
	$effect(() => {
		const targets = SECTIONS.map((s) => document.getElementById(s.id)).filter((el) => el !== null);
		const observer = new IntersectionObserver(
			(entries) => {
				const visible = entries.filter((e) => e.isIntersecting);
				if (visible.length > 0) current = visible[0].target.id;
			},
			{ rootMargin: '-10% 0px -75% 0px' }
		);
		for (const el of targets) observer.observe(el);
		return () => observer.disconnect();
	});

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
	/** Whether the server took it. On a refusal the switch goes back to where it was. */
	async function savePreferences(): Promise<boolean> {
		const res = await api.put('/me/preferences', { showNsfw, archiveLinks });
		if (res.ok) toast.success('Saved.');
		else toast.error(failureMessage(res, 'Could not save that setting.'));
		return res.ok;
	}

	async function setNsfw(value: boolean) {
		showNsfw = value;
		if (!(await savePreferences())) showNsfw = !value;
	}

	async function setArchive(value: boolean) {
		archiveLinks = value;
		if (!(await savePreferences())) archiveLinks = !value;
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

<Page>
	<PageHeader
		title="Settings"
		description="Your account, how Linkbelli looks, what it emails you, and the ways your links get in and out."
	/>

	<div class="mt-6 md:grid md:grid-cols-[11rem_minmax(0,1fr)] md:gap-10">
		<!-- A row of tabs across the top on a phone, a list down the side on a wider screen; both
		     stay in view while the page scrolls. -->
		<nav
			aria-label="Settings sections"
			class="sticky top-0 z-(--z-sticky) -mx-4 mb-8 overflow-x-auto border-b bg-bg px-4 md:top-8 md:mx-0 md:mb-0 md:self-start md:overflow-visible md:border-b-0 md:bg-transparent md:px-0"
		>
			<ul class="flex gap-1 py-2 md:flex-col md:py-0">
				{#each SECTIONS as section (section.id)}
					<li>
						<a
							href={`#${section.id}`}
							onclick={() => (current = section.id)}
							aria-current={current === section.id ? 'location' : undefined}
							class="block rounded-control px-3 py-2 text-sm whitespace-nowrap hover:bg-black/5 dark:hover:bg-white/10 {current ===
							section.id
								? 'bg-selected font-medium text-accent'
								: 'text-muted'} {section.id === 'close' ? 'md:mt-4' : ''}"
						>
							{section.label}
						</a>
					</li>
				{/each}
			</ul>
		</nav>

		<div class="flex min-w-0 flex-col gap-16">
			<section id="account" aria-labelledby="account-heading" class="flex scroll-mt-20 flex-col gap-6 md:scroll-mt-8">
				<h2 id="account-heading" class="t-group">Account</h2>
				{#if data.user}
					<!-- minmax(0, 1fr) and wrapping anywhere: an email address is one unbreakable word,
					     and a plain 1fr column refuses to be narrower than it — which made the whole page
					     scroll sideways on a phone. -->
					<dl class="grid grid-cols-[8rem_minmax(0,1fr)] gap-y-2 rounded-card border bg-surface p-4 text-sm">
						<dt class="text-muted">Username</dt>
						<dd class="[overflow-wrap:anywhere]">{data.user.username ?? '—'}</dd>
						<dt class="text-muted">Email</dt>
						<dd class="[overflow-wrap:anywhere]">{data.user.email ?? '—'}</dd>
					</dl>
				{/if}

				<ChangePasswordForm />
			</section>

			<section id="appearance" aria-labelledby="appearance-heading" class="flex scroll-mt-20 flex-col gap-8 md:scroll-mt-8">
				<h2 id="appearance-heading" class="t-group">Appearance & content</h2>

				<div>
					<h3 class="t-section">Theme</h3>
					<div class="mt-3">
						<ThemeToggle initial={data.theme} />
					</div>
				</div>

				<div>
					<h3 class="t-section">Content</h3>
					<label class="mt-3 flex items-center gap-2 text-sm">
						<Switch checked={showNsfw} onchange={setNsfw} labelledby="pref-nsfw" />
						<span id="pref-nsfw">Show adult (NSFW) content</span>
					</label>
				</div>

				<div>
					<h3 class="t-section">Archiving</h3>
					<label class="mt-3 flex items-center gap-2 text-sm">
						<Switch checked={archiveLinks} onchange={setArchive} labelledby="pref-archive" />
						<span id="pref-archive">Keep a public snapshot of pages I save</span>
					</label>
					<!-- Off by default, and the reason is worth saying out loud rather than burying:
					     this sends addresses to someone else. -->
					<p class="mt-2 max-w-prose text-sm text-muted">
						Asks the Internet Archive for a copy, so a link still leads somewhere once the
						original is gone. It means sending the addresses you save to a third party, and the
						snapshots are public — which is why this is off unless you turn it on.
					</p>
				</div>
			</section>

			<section id="email" aria-labelledby="email-heading" class="flex scroll-mt-20 flex-col gap-6 md:scroll-mt-8">
				<h2 id="email-heading" class="t-group">Email</h2>
				<NotificationsPanel
					email={data.user?.email}
					confirmed={data.user?.emailConfirmed ?? true}
					initial={data.notifications}
				/>
			</section>

			<section id="data" aria-labelledby="data-heading" class="flex scroll-mt-20 flex-col gap-8 md:scroll-mt-8">
				<h2 id="data-heading" class="t-group">Your data</h2>

				{#if data.usage}
					<div>
						<h3 class="t-section">What you have here</h3>
						<dl class="mt-3 grid grid-cols-2 gap-x-6 gap-y-3 rounded-card border bg-surface p-4 text-sm sm:grid-cols-4">
							{#each usageEntries as entry (entry.label)}
								<div>
									<dt class="text-muted">{entry.label}</dt>
									<dd class="text-lg font-semibold tabular-nums">
										{entry.value}
										{#if entry.href && entry.value > 0}
											<a href={entry.href} class="ml-1 text-xs font-normal text-accent underline underline-offset-2">
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
						<h3 class="t-section">Quota</h3>
						<div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
							{#each [
								{ label: 'Sources', used: data.quota.sourcesUsed, max: data.quota.maxSources },
								{ label: 'Runs today', used: data.quota.runsUsedToday, max: data.quota.maxRunsPerDay },
								{ label: 'Items / run', used: 0, max: data.quota.maxItemsPerRun }
							] as q (q.label)}
								<div class="rounded-card border bg-surface p-3">
									<div class="text-sm text-muted">{q.label}</div>
									<div class="mt-1 text-lg font-semibold">
										{#if q.label === 'Items / run'}{q.max}{:else}{q.used} / {q.max}{/if}
									</div>
									{#if q.label !== 'Items / run'}
										<div class="mt-2 h-1.5 overflow-hidden rounded-full bg-border">
											<div class="h-full bg-accent-solid" style="width: {pct(q.used, q.max)}%"></div>
										</div>
									{/if}
								</div>
							{/each}
						</div>
					</div>
				{/if}

				<div>
					<h3 class="t-section">Export</h3>
					<p class="mt-1 text-sm text-muted">
						Download everything you have here. It is your data; take it wherever you like.
					</p>
					<div class="mt-3 flex flex-wrap gap-2">
						{#each EXPORTS as fmt (fmt.format)}
							<Button href={`/api/v1/export?format=${fmt.format}`} download icon={Download} title={fmt.hint}>
								{fmt.label}
							</Button>
						{/each}
					</div>
				</div>

				<BackupsPanel enabled={data.user?.backupsEnabled ?? true} initial={data.backups} />
			</section>

			<section id="integrations" aria-labelledby="integrations-heading" class="flex scroll-mt-20 flex-col gap-8 md:scroll-mt-8">
				<h2 id="integrations-heading" class="t-group">Integrations</h2>

				<div>
					<h3 class="t-section">Save from anywhere</h3>
					<p class="mt-1 text-sm text-muted">
						Drag this to your bookmarks bar. Clicking it on any page opens Linkbelli with the
						address already filled in — no extension needed.
					</p>
					<p class="mt-3">
						<!-- A javascript: href is exactly what a bookmarklet is; it never runs from this page. -->
						<a
							href={bookmarklet}
							onclick={(e) => e.preventDefault()}
							class="inline-flex cursor-grab items-center gap-1.5 rounded-control border border-accent px-3 py-2 text-sm font-medium text-accent"
							title="Drag me to your bookmarks bar"
						>
							<Bookmark size={15} aria-hidden="true" /> Save to Linkbelli
						</a>
					</p>
					<p class="mt-2 text-xs text-muted">
						On a phone, install Linkbelli to your home screen and it shows up in the system share
						sheet.
					</p>
					<div class="mt-2">
						<OfflineSupportNotice compact detail />
					</div>
				</div>

				<ApiKeysManager keys={data.apiKeys} />

				<WebhooksPanel initial={data.webhooks} initialEvents={data.webhookEvents} />
			</section>

			<!-- The counterpart of the export formats. Kept apart from everything else, at the end,
			     where nobody reaches it on the way to something else. -->
			<section
				id="close"
				aria-labelledby="close-heading"
				class="scroll-mt-20 rounded-card border border-danger p-5 md:scroll-mt-8"
			>
				<h2 id="close-heading" class="t-group text-danger">Close this account</h2>

				{#if leavingAt}
					<p class="mt-2 max-w-prose text-sm">
						Scheduled for {new Date(leavingAt).toLocaleDateString(undefined, {
							day: 'numeric',
							month: 'long',
							year: 'numeric'
						})}. Everything you published is already hidden and your sources have stopped.
					</p>
					<p class="mt-1 max-w-prose text-sm text-muted">
						Sign in again before then and it is called off — nothing is lost, and what you had
						published goes back up exactly as it was.
					</p>
				{:else}
					<p class="mt-2 max-w-prose text-sm text-muted">
						Your playlists, links, sources, folders, keys and backups all go, after
						{GRACE_DAYS} days. Take an export first — once it runs there is nothing to come back to.
					</p>
					<p class="mt-1 max-w-prose text-sm text-muted">
						Links you saved that other people also saved stay, because they are the same rows;
						so does any copy somebody took of a playlist you published, which is theirs now.
					</p>

					<form class="mt-4 flex flex-wrap items-end gap-2" onsubmit={leave}>
						<Field label="Confirm with your password" error={leaveError ?? undefined}>
							{#snippet children(f)}
								<Input
									id={f.id}
									type="password"
									bind:value={password}
									autocomplete="current-password"
									aria-describedby={f.describedby}
									aria-invalid={f.invalid}
								/>
							{/snippet}
						</Field>
						<Button type="submit" variant="danger-outline" loading={leaving} disabled={!password}>
							{leaving ? 'Closing…' : 'Close my account'}
						</Button>
					</form>
				{/if}
			</section>
		</div>
	</div>
</Page>
