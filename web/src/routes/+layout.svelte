<script lang="ts">
	import Button from '$lib/components/ui/Button.svelte';
	import { palette } from '$lib/overlays.svelte';
	import ShortcutsDialog from '$lib/components/ShortcutsDialog.svelte';
	import { Dialog, Popover } from 'bits-ui';
	import NavigationProgress from '$lib/components/NavigationProgress.svelte';
	import { buttonClass } from '$lib/components/ui/Button.svelte';
	import '../app.css';
		import { Bookmark, Home, ListMusic, Rss, Compass, Upload, User, LogOut, PanelLeftClose, PanelLeft, Menu, Search, ListChecks, Wand2, Gauge, Highlighter, X, Newspaper, Tags, CopyCheck, Trash2, Folder as FolderIcon } from '@lucide/svelte';
	import { page } from '$app/state';
	import { onMount } from 'svelte';
	import { afterNavigate } from '$app/navigation';
	import GlobalDialog from '$lib/components/GlobalDialog.svelte';
	import Toaster from '$lib/components/Toaster.svelte';
	import CommandPalette from '$lib/components/CommandPalette.svelte';
	import FolderTree from '$lib/components/FolderTree.svelte';
	import OfflineQueueBanner from '$lib/components/OfflineQueueBanner.svelte';
	import { watchConnection } from '$lib/offlineSaves.svelte';
	import { registerServiceWorker } from '$lib/serviceWorker.svelte';
	import type { LayoutData } from './$types';

	let { data, children }: { data: LayoutData; children: import('svelte').Snippet } = $props();

	const inSection = (path: string, prefix: string) =>
		path === prefix || path.startsWith(prefix + '/');

	// Top-level destinations. Home is the site introduction; Playlists and Sources are the two
	// first-class working areas. Folders live at /folders/:id but belong to the Playlists section.
	type NavItem = {
		href: string;
		label: string;
		Icon: typeof Home;
		match: (p: string) => boolean;
		/** A number worth a glance, shown beside the label. */
		count?: () => number;
	};

	/**
	 * Where things live, in groups by what they are for.
	 *
	 * Ten destinations used to sit in one flat list — two of them with the same feed icon, so the
	 * collapsed rail showed Sources and Feed as the same thing — while Tags, Duplicates and
	 * Trash were only reachable through unlabelled icons on the playlists page. Grouped now:
	 * what you keep and read, what brings things in, what other people share, and tools.
	 */
	const NAV: { heading: string | null; items: NavItem[] }[] = [
		{
			heading: null,
			items: [{ href: '/', label: 'Home', Icon: Home, match: (p) => p === '/' }]
		},
		{
			heading: 'Library',
			items: [
				{
					href: '/playlists',
					label: 'Playlists',
					Icon: ListMusic,
					// Folders belong to the playlists they hold.
					match: (p) => inSection(p, '/playlists') || inSection(p, '/folders')
				},
				{ href: '/queue', label: 'Up next', Icon: ListChecks, match: (p) => inSection(p, '/queue') },
				{ href: '/highlights', label: 'Highlights', Icon: Highlighter, match: (p) => inSection(p, '/highlights') },
				{ href: '/search', label: 'Search', Icon: Search, match: (p) => inSection(p, '/search') },
				{ href: '/tags', label: 'Tags', Icon: Tags, match: (p) => inSection(p, '/tags') }
			]
		},
		{
			heading: 'Bringing links in',
			items: [
				{ href: '/sources', label: 'Sources', Icon: Rss, match: (p) => inSection(p, '/sources') },
				// Next to Sources deliberately: rules act on what sources bring in.
				{ href: '/automations', label: 'Rules', Icon: Wand2, match: (p) => inSection(p, '/automations') },
				{ href: '/import', label: 'Import', Icon: Upload, match: (p) => inSection(p, '/import') }
			]
		},
		{
			heading: 'Community',
			items: [
				{
					href: '/feed',
					label: 'Feed',
					Icon: Newspaper,
					match: (p) => inSection(p, '/feed'),
					count: () => data.feedNew
				},
				{ href: '/discover', label: 'Discover', Icon: Compass, match: (p) => inSection(p, '/discover') }
			]
		},
		{
			heading: 'Tidy up',
			items: [
				{ href: '/duplicates', label: 'Duplicates', Icon: CopyCheck, match: (p) => inSection(p, '/duplicates') },
				{ href: '/trash', label: 'Trash', Icon: Trash2, match: (p) => inSection(p, '/trash') }
			]
		}
	];

	const onSettings = $derived(inSection(page.url.pathname, '/settings'));
	const onAdmin = $derived(inSection(page.url.pathname, '/admin'));
	const isAdmin = $derived(data.user?.roles?.includes('Admin') ?? false);
	/**
	 * Pages that are one card: signing in and up, and the places an email link lands.
	 *
	 * Only sign-in and sign-up used to be centred. The rest — a password reset, a confirmed
	 * address, an unsubscribe, an invitation — sat in the top-left corner under the brand bar, or
	 * pressed against the sidebar for somebody signed in, and looked like a page that had not
	 * finished loading.
	 */
	const CARD_PAGES = ['/login', '/register', '/forgot-password', '/reset-password', '/confirm-email', '/unsubscribe'];
	const isCardPage = $derived(
		CARD_PAGES.includes(page.url.pathname) || page.url.pathname.startsWith('/invite/')
	);
	// Anonymous card pages get the centred layout; other anonymous pages (public playlists,
	// discover) get a normal top-aligned container with a brand bar.
	const isAuthPage = $derived(isCardPage);

	// An embed is a card inside somebody else's page. It gets none of the app around it — no
	// brand bar, no sidebar, no dialogs or palette — whoever is looking at it.
	const isEmbed = $derived(page.url.pathname.startsWith('/embed/'));

	// onMount, not $effect: this is one-time setup, and as an effect it looped — flushing the
	// queue reads and writes the same state the effect was tracking, so each flush re-ran it.
	onMount(() => {
		// The worker and the save queue belong to the app, not to a card framed on another site.
		if (page.url.pathname.startsWith('/embed/')) return;

		registerServiceWorker();

		// A link queued on the share sheet has to be sent the moment a connection returns,
		// whatever the person happens to be looking at by then.
		return watchConnection();
	});

	// Mobile nav drawer (below md the sidebar is hidden). Close it after navigating.
	let drawerOpen = $state(false);
	afterNavigate((navigation) => {
		drawerOpen = false;

		// A new page moves focus to its content, so a keyboard or screen-reader user lands on
		// what they asked for rather than back at the top of the sidebar. Only between pages: a
		// filter or search that changes just the query keeps focus where the typing is.
		const from = navigation.from?.url.pathname;
		const to = navigation.to?.url.pathname;
		if (navigation.type !== 'enter' && from && to && from !== to) {
			document.getElementById('main')?.focus({ preventScroll: true });
		}
	});

	// Desktop sidebar can be minimized to an icon-only rail.
	// Remembered across visits, in a cookie the server reads so the first paint is right.
	let collapsed = $state(data.sidebarCollapsed ?? false);

	function toggleSidebar() {
		collapsed = !collapsed;
		document.cookie = `lb_sidebar=${collapsed ? 'collapsed' : 'expanded'}; path=/; max-age=31536000; samesite=lax`;
	}
</script>

<!-- Shared nav body — rendered in both the desktop sidebar (collapsible) and the mobile drawer (always expanded). -->
{#snippet navBody(showLabels = true)}
	<!-- Everything you can go to scrolls; the account row below stays put.
	     The whole rail used to be one scrolling column with the footer pushed to the end of it by
	     mt-auto, which does nothing once the content is taller than the box. At 1440x900 — with no
	     folders and no saved searches — the rail ran 968px against 900 of screen, and the account
	     link and Sign out sat at y=900 and y=904: entirely below the fold, with no scrollbar and
	     nothing to say there was more. -->
	<div class="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain">
	<!-- Says the key it answers, because that is how anybody learns it.

	     showLabels, not `collapsed` — the drawer renders this snippet with labels on, and reading
	     the sidebar's own cookie meant somebody who had collapsed the rail on their laptop got a
	     bare magnifying glass in the drawer on their phone, unlabelled among twelve labelled
	     items. The key hint goes on a touch screen, which has no Ctrl to press. -->
		<button
			type="button"
			onclick={() => palette.show()}
			class="mb-3 flex items-center gap-2 rounded-control border border-border px-3 py-2 text-sm text-muted hover:bg-black/5 dark:hover:bg-white/10"
			class:justify-center={!showLabels}
			title="Search, or jump to a playlist"
		>
			<Search size={16} aria-hidden="true" />
			{#if showLabels}
				<span class="flex-1 text-left">Search or jump…</span>
				<kbd class="rounded-control border border-border px-1 text-xs pointer-coarse:hidden">Ctrl K</kbd>
			{:else}
				<span class="sr-only">Search, or jump to a playlist</span>
			{/if}
		</button>

		<nav aria-label="Main" class="flex flex-col gap-3 text-base">
		{#each NAV as group, g (group.heading ?? g)}
			<div class="flex flex-col gap-0.5">
				{#if group.heading}
					{#if showLabels}
						<p class="px-3 pb-0.5 text-xs font-medium text-muted">{group.heading}</p>
					{:else}
						<!-- In the rail, a rule stands in for the heading. -->
						<hr class="mx-3 mb-1 border-border" />
					{/if}
				{/if}
				<!-- The tooltip is for the rail, where the icon is all there is: expanded, it popped a
				     native tooltip repeating the word already sitting beside it. -->
				{#each group.items as item (item.href)}
					{@const active = item.match(page.url.pathname)}
					{@const count = item.count?.() ?? 0}
					<a
						href={item.href}
						class="relative flex items-center gap-3 rounded-control px-3 py-2 hover:bg-black/5 dark:hover:bg-white/10"
						class:font-medium={active}
						style={active ? 'background: var(--color-border)' : ''}
						aria-current={active ? 'page' : undefined}
						title={showLabels ? undefined : item.label}
					>
						<item.Icon size={20} aria-hidden="true" />
						{#if showLabels}<span class="flex-1">{item.label}</span>{/if}
						{#if count > 0}
							{#if showLabels}
								<span class="rounded-full bg-accent-solid px-1.5 text-xs font-medium text-on-solid tabular-nums">
									{count}
								</span>
							{:else}
								<span class="absolute top-1.5 right-2 size-2 rounded-full bg-accent-solid" aria-hidden="true"></span>
							{/if}
							<span class="sr-only">({count} new)</span>
						{/if}
					</a>
				{/each}
			</div>
		{/each}
	</nav>

	{#if !showLabels && (data.pinned.length > 0 || data.folders.length > 0)}
		<!-- Collapsed to a rail, the saved searches and the folder tree used to disappear
		     altogether. Each is one button away instead. -->
		<div class="mt-3 flex flex-col gap-0.5 border-t border-border pt-3">
			{#if data.pinned.length > 0}
				<Popover.Root>
					<Popover.Trigger
						class="flex items-center justify-center rounded-control px-3 py-2 hover:bg-black/5 dark:hover:bg-white/10"
						title="Saved searches"
						aria-label="Saved searches"
					>
						<Bookmark size={20} aria-hidden="true" />
					</Popover.Trigger>
					<Popover.Content side="right" sideOffset={8} class="popover-surface z-(--z-popover) w-64 rounded-card border p-2 shadow-popover">
						<p class="px-2 pb-1.5 text-xs font-medium text-muted">Saved searches</p>
						{#each data.pinned as saved (saved.id)}
							<a
								href={`/search?saved=${saved.id}`}
								class="flex items-center gap-2 rounded-control px-2 py-1.5 text-sm hover:bg-black/5 dark:hover:bg-white/10"
							>
								<span class="min-w-0 flex-1 truncate">{saved.name}</span>
								<span class="shrink-0 text-xs text-muted tabular-nums">{saved.count}</span>
							</a>
						{/each}
					</Popover.Content>
				</Popover.Root>
			{/if}
			{#if data.folders.length > 0}
				<Popover.Root>
					<Popover.Trigger
						class="flex items-center justify-center rounded-control px-3 py-2 hover:bg-black/5 dark:hover:bg-white/10"
						title="Folders"
						aria-label="Folders"
					>
						<FolderIcon size={20} aria-hidden="true" />
					</Popover.Trigger>
					<Popover.Content side="right" sideOffset={8} class="popover-surface z-(--z-popover) max-h-[70vh] w-64 overflow-y-auto rounded-card border p-2 shadow-popover">
						<p class="px-2 pb-1.5 text-xs font-medium text-muted">Folders</p>
						<FolderTree folders={data.folders} />
					</Popover.Content>
				</Popover.Root>
			{/if}
		</div>
	{/if}

	{#if showLabels && data.pinned.length > 0}
		<!-- A saved search was a question you re-asked by hand from the search page, so
		     "everything unread from these five sites under ten minutes" could be asked but not
		     had. With a number beside it, it is a thing you look at. -->
		<div class="mt-4 border-t pt-3" style="border-color: var(--color-border)">
			<p class="px-3 pb-1.5 text-xs font-medium" style="color: var(--color-muted)">Searches</p>
			<nav aria-label="Saved searches" class="flex flex-col gap-0.5 text-sm">
				{#each data.pinned as saved (saved.id)}
					<a
						href={`/search?saved=${saved.id}`}
						class="flex items-center gap-2 rounded-control px-3 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
						title={saved.name}
					>
						<Bookmark size={15} aria-hidden="true" class="shrink-0" />
						<span class="min-w-0 flex-1 truncate">{saved.name}</span>
						<span class="shrink-0 text-xs tabular-nums" style="color: var(--color-muted)">
							{saved.count}
						</span>
					</a>
				{/each}
			</nav>
		</div>
	{/if}

	{#if showLabels && data.folders.length > 0}
		<!-- Folders are a ten-deep tree in the API and were only reachable one page at a time. -->
		<div class="mt-4 border-t pt-3" style="border-color: var(--color-border)">
			<p class="px-3 pb-1.5 text-xs font-medium" style="color: var(--color-muted)">Folders</p>
			<FolderTree folders={data.folders} />
		</div>
	{/if}

	</div>

	<div class="mt-3 flex shrink-0 {showLabels ? 'items-center gap-1' : 'flex-col gap-1'} border-t pt-3" style="border-color: var(--color-border)">
		<a
			href="/settings"
			class="flex min-w-0 items-center gap-3 rounded-control px-3 py-2.5 hover:bg-black/5 dark:hover:bg-white/10 {showLabels ? 'flex-1' : 'justify-center'}"
			class:font-medium={onSettings}
			style={onSettings ? 'background: var(--color-border)' : ''}
			aria-current={onSettings ? 'page' : undefined}
			title="Account settings"
			aria-label={`Account settings for ${data.user?.username ?? 'you'}`}
		>
			<User size={20} aria-hidden="true" />
			{#if showLabels}<span class="truncate" style="color: var(--color-muted)">{data.user?.username}</span>{/if}
		</a>
		{#if isAdmin}
			<!-- Shown only where it will work: the API is still the authority on who may look. -->
			<a
				href="/admin"
				class="rounded-control p-2.5 hover:bg-black/5 dark:hover:bg-white/10"
				class:font-medium={onAdmin}
				style={onAdmin ? 'background: var(--color-border)' : ''}
				aria-current={onAdmin ? 'page' : undefined}
				title="Instance overview"
				aria-label="Instance overview"
			>
				<Gauge size={20} aria-hidden="true" />
			</a>
		{/if}
		<form method="post" action="/logout" class={showLabels ? '' : 'flex justify-center'}>
			<button
				type="submit"
				class="rounded-control p-2.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="Sign out"
				aria-label="Sign out"
			>
				<LogOut size={20} aria-hidden="true" />
			</button>
		</form>
	</div>
{/snippet}

{#if !isEmbed}
	<!-- The first stop for the keyboard: straight past the navigation to the page itself. -->
	<a
		href="#main"
		class="sr-only rounded-control border border-accent bg-surface px-3 py-2 text-sm font-medium focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-(--z-toast)"
	>
		Skip to content
	</a>
{/if}

{#if isEmbed}
	{@render children()}
{:else if data.user}
	<div class="flex min-h-screen flex-col md:flex-row">
		<!-- Mobile top bar with hamburger; hidden at md+ where the sidebar shows. -->
		<header
			class="flex items-center gap-3 border-b px-4 py-3 md:hidden"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<Dialog.Root bind:open={drawerOpen}>
				<Dialog.Trigger
					class="-ml-1 rounded-control p-2 hover:bg-black/5 dark:hover:bg-white/10"
					aria-label="Open navigation menu"
				>
					<Menu size={23} aria-hidden="true" />
				</Dialog.Trigger>
				<Dialog.Portal>
					<Dialog.Overlay class="anim-fade fixed inset-0 z-(--z-overlay) bg-black/40" />
					<!-- overflow-y-auto, because the list is taller than a phone: at 375×812 the drawer
					     ran 954px and the container did not scroll, so Trash, the account link and Sign
					     out simply had nowhere to be — and the page behind is scroll-locked, so there was
					     no way to reach them at all. On a 667px screen six destinations were off the
					     bottom. overscroll-contain keeps the swipe from chaining to that locked page. -->
					<Dialog.Content
						class="anim-slide-left fixed inset-y-0 left-0 z-(--z-modal) flex w-72 flex-col gap-1 overflow-hidden border-r p-4"
						style="border-color: var(--color-border); background: var(--color-surface)"
					>
						<div class="flex shrink-0 items-center justify-between gap-2 px-2 pb-4">
							<Dialog.Title class="text-lg font-semibold">Linkbelli</Dialog.Title>
							<!-- A visible way out: closing by Escape or a tap outside is not obvious, and
							     not always available to a screen-reader user. -->
							<Dialog.Close class={buttonClass('ghost', 'md', true)} title="Close menu" aria-label="Close menu">
								<X size={20} aria-hidden="true" />
							</Dialog.Close>
						</div>
						{@render navBody(true)}
					</Dialog.Content>
				</Dialog.Portal>
			</Dialog.Root>
			<a href="/" class="flex-1 truncate text-lg font-semibold">Linkbelli</a>
			<!-- On a phone the palette answered two keys and nothing else, which is no way in
			     at all. -->
			<Button
				variant="ghost"
				size="sm"
				icon={Search}
				iconOnly
				label="Search, or jump to a playlist"
				onclick={() => palette.show()}
			/>
		</header>

		<aside
			class={`hidden shrink-0 flex-col gap-1 border-r p-4 md:flex sticky top-0 h-screen overflow-hidden ${collapsed ? 'w-20' : 'w-72'}`}
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex shrink-0 items-center gap-2 px-2 pb-4">
				{#if !collapsed}<a href="/" class="flex-1 truncate text-lg font-semibold">Linkbelli</a>{/if}
				<button
					type="button"
					onclick={toggleSidebar}
					class="rounded-control p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
					aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
				>
					{#if collapsed}
						<PanelLeft size={20} aria-hidden="true" />
					{:else}
						<PanelLeftClose size={20} aria-hidden="true" />
					{/if}
				</button>
			</div>
			{@render navBody(!collapsed)}
		</aside>

		<main id="main" tabindex="-1" class="min-w-0 flex-1 p-4 outline-none md:p-8">
			<OfflineQueueBanner />
			{#if isCardPage}
				<div class="flex min-h-[70vh] items-center justify-center">
					{@render children()}
				</div>
			{:else}
				{@render children()}
			{/if}
		</main>
	</div>
{:else if isAuthPage}
	<main id="main" tabindex="-1" class="flex min-h-screen flex-col items-center justify-center gap-6 p-6 outline-none">
		<!-- Somebody arriving from an email has nothing else to say where they are. -->
		<a href="/" class="text-lg font-semibold">Linkbelli</a>
		{@render children()}
	</main>
{:else}
	<div class="flex min-h-screen flex-col">
		<header class="flex items-center justify-between border-b px-6 py-3" style="border-color: var(--color-border)">
			<a href="/" class="text-lg font-semibold">Linkbelli</a>
			<!-- Signing in was the only thing on offer to somebody who has no account yet. -->
			<span class="flex items-center gap-2">
				<a href="/login" class="text-sm font-medium" style="color: var(--color-accent)">Sign in</a>
				<a
					href="/register"
					class="rounded-control bg-accent-solid px-3 py-1.5 text-sm font-medium text-on-solid hover:brightness-110"
				>
					Create account
				</a>
			</span>
		</header>
		<main id="main" tabindex="-1" class="mx-auto w-full max-w-5xl flex-1 p-6 outline-none">
			{@render children()}
		</main>
	</div>
{/if}

{#if !isEmbed}
	<NavigationProgress />
	<GlobalDialog />
	<Toaster />
	{#if data.user}
		<!-- Signed-in only: everything they offer needs an account. -->
		<CommandPalette />
		<ShortcutsDialog />
	{/if}
{/if}
