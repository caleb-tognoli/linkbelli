<script lang="ts">
	import NavigationProgress from '$lib/components/NavigationProgress.svelte';
	import { buttonClass } from '$lib/components/ui/Button.svelte';
	import '../app.css';
	import { Dialog } from 'bits-ui';
	import { Bookmark, Home, ListMusic, Rss, Compass, Upload, User, LogOut, PanelLeftClose, PanelLeft, Menu, Search, ListChecks, Wand2, Gauge, Highlighter, X } from '@lucide/svelte';
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
	const NAV = [
		{ href: '/', label: 'Home', Icon: Home, match: (p: string) => p === '/' },
		{
			href: '/playlists',
			label: 'Playlists',
			Icon: ListMusic,
			match: (p: string) => inSection(p, '/playlists') || inSection(p, '/folders')
		},
		{ href: '/sources', label: 'Sources', Icon: Rss, match: (p: string) => inSection(p, '/sources') },
		// Next to Sources deliberately: rules act on what sources bring in.
		{ href: '/automations', label: 'Rules', Icon: Wand2, match: (p: string) => inSection(p, '/automations') },
		{ href: '/search', label: 'Search', Icon: Search, match: (p: string) => inSection(p, '/search') },
		{ href: '/queue', label: 'Up next', Icon: ListChecks, match: (p: string) => inSection(p, '/queue') },
		{
			href: '/highlights',
			label: 'Highlights',
			Icon: Highlighter,
			match: (p: string) => inSection(p, '/highlights')
		},
		{ href: '/feed', label: 'Feed', Icon: Rss, match: (p: string) => inSection(p, '/feed') },
		{ href: '/discover', label: 'Discover', Icon: Compass, match: (p: string) => inSection(p, '/discover') },
		{ href: '/import', label: 'Import', Icon: Upload, match: (p: string) => inSection(p, '/import') }
	];

	const onSettings = $derived(inSection(page.url.pathname, '/profile'));
	const onAdmin = $derived(inSection(page.url.pathname, '/admin'));
	const isAdmin = $derived(data.user?.roles?.includes('Admin') ?? false);
	// Anonymous auth pages (login/register) get centered card chrome; other anonymous pages
	// (public playlist view, discover) get a normal top-aligned container with a brand bar.
	const isAuthPage = $derived(['/login', '/register'].includes(page.url.pathname));

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
	let collapsed = $state(false);
</script>

<!-- Shared nav body — rendered in both the desktop sidebar (collapsible) and the mobile drawer (always expanded). -->
{#snippet navBody(showLabels = true)}
	<nav aria-label="Main" class="flex flex-col gap-1 text-base">
		{#each NAV as item (item.href)}
			{@const active = item.match(page.url.pathname)}
			<a
				href={item.href}
				class="flex items-center gap-3 rounded-md px-3 py-2.5 hover:bg-black/5 dark:hover:bg-white/10"
				class:font-medium={active}
				style={active ? 'background: var(--color-border)' : ''}
				aria-current={active ? 'page' : undefined}
				title={item.label}
			>
				<item.Icon size={20} aria-hidden="true" />
				{#if showLabels}<span>{item.label}</span>{/if}
			</a>
		{/each}
	</nav>

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
						class="flex items-center gap-2 rounded-md px-3 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
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

	<div class="mt-auto flex {showLabels ? 'items-center gap-1' : 'flex-col gap-1'} border-t pt-3" style="border-color: var(--color-border)">
		<a
			href="/profile"
			class="flex min-w-0 items-center gap-3 rounded-md px-3 py-2.5 hover:bg-black/5 dark:hover:bg-white/10 {showLabels ? 'flex-1' : 'justify-center'}"
			class:font-medium={onSettings}
			style={onSettings ? 'background: var(--color-border)' : ''}
			aria-current={onSettings ? 'page' : undefined}
			title={data.user?.username ?? 'Account settings'}
		>
			<User size={20} aria-hidden="true" />
			{#if showLabels}<span class="truncate" style="color: var(--color-muted)">{data.user?.username}</span>{/if}
		</a>
		{#if isAdmin}
			<!-- Shown only where it will work: the API is still the authority on who may look. -->
			<a
				href="/admin"
				class="rounded-md p-2.5 hover:bg-black/5 dark:hover:bg-white/10"
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
				class="rounded-md p-2.5 hover:bg-black/5 dark:hover:bg-white/10"
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
					class="-ml-1 rounded p-2 hover:bg-black/5 dark:hover:bg-white/10"
					aria-label="Open navigation menu"
				>
					<Menu size={23} aria-hidden="true" />
				</Dialog.Trigger>
				<Dialog.Portal>
					<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
					<Dialog.Content
						class="fixed inset-y-0 left-0 z-50 flex w-72 flex-col gap-1 border-r p-4"
						style="border-color: var(--color-border); background: var(--color-surface)"
					>
						<div class="flex items-center justify-between gap-2 px-2 pb-4">
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
			<span class="text-lg font-semibold">Linkbelli</span>
		</header>

		<aside
			class={`hidden shrink-0 flex-col gap-1 border-r p-4 md:flex sticky top-0 h-screen overflow-y-auto ${collapsed ? 'w-20' : 'w-72'}`}
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="flex items-center gap-2 px-2 pb-4">
				{#if !collapsed}<span class="flex-1 truncate text-lg font-semibold">Linkbelli</span>{/if}
				<button
					type="button"
					onclick={() => (collapsed = !collapsed)}
					class="rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
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
			{@render children()}
		</main>
	</div>
{:else if isAuthPage}
	<main id="main" tabindex="-1" class="flex min-h-screen items-center justify-center p-6 outline-none">
		{@render children()}
	</main>
{:else}
	<div class="flex min-h-screen flex-col">
		<header class="flex items-center justify-between border-b px-6 py-3" style="border-color: var(--color-border)">
			<a href="/" class="text-lg font-semibold">Linkbelli</a>
			<a href="/login" class="text-sm font-medium" style="color: var(--color-accent)">Sign in</a>
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
		<!-- Signed-in only: everything it offers needs an account. -->
		<CommandPalette />
	{/if}
{/if}
