<script lang="ts">
	import '../app.css';
	import { Dialog } from 'bits-ui';
	import { Home, ListMusic, Rss, Compass, Upload, User, LogOut, PanelLeftClose, PanelLeft, Menu, Search, ListChecks } from '@lucide/svelte';
	import { page } from '$app/state';
	import { afterNavigate } from '$app/navigation';
	import GlobalDialog from '$lib/components/GlobalDialog.svelte';
	import CommandPalette from '$lib/components/CommandPalette.svelte';
	import FolderTree from '$lib/components/FolderTree.svelte';
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
		{ href: '/search', label: 'Search', Icon: Search, match: (p: string) => inSection(p, '/search') },
		{ href: '/queue', label: 'Up next', Icon: ListChecks, match: (p: string) => inSection(p, '/queue') },
		{ href: '/discover', label: 'Discover', Icon: Compass, match: (p: string) => inSection(p, '/discover') },
		{ href: '/import', label: 'Import', Icon: Upload, match: (p: string) => inSection(p, '/import') }
	];

	const onSettings = $derived(inSection(page.url.pathname, '/profile'));
	// Anonymous auth pages (login/register) get centered card chrome; other anonymous pages
	// (public playlist view, discover) get a normal top-aligned container with a brand bar.
	const isAuthPage = $derived(['/login', '/register'].includes(page.url.pathname));

	// Mobile nav drawer (below md the sidebar is hidden). Close it after navigating.
	let drawerOpen = $state(false);
	afterNavigate(() => (drawerOpen = false));

	// Desktop sidebar can be minimized to an icon-only rail.
	let collapsed = $state(false);
</script>

<!-- Shared nav body — rendered in both the desktop sidebar (collapsible) and the mobile drawer (always expanded). -->
{#snippet navBody(showLabels = true)}
	<nav class="flex flex-col gap-1 text-base">
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

{#if data.user}
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
						<Dialog.Title class="px-2 pb-4 text-lg font-semibold">Linkbelli</Dialog.Title>
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

		<main class="flex-1 p-4 md:p-8">
			{@render children()}
		</main>
	</div>
{:else if isAuthPage}
	<main class="flex min-h-screen items-center justify-center p-6">
		{@render children()}
	</main>
{:else}
	<div class="flex min-h-screen flex-col">
		<header class="flex items-center justify-between border-b px-6 py-3" style="border-color: var(--color-border)">
			<a href="/" class="text-lg font-semibold">Linkbelli</a>
			<a href="/login" class="text-sm font-medium" style="color: var(--color-accent)">Sign in</a>
		</header>
		<main class="mx-auto w-full max-w-5xl flex-1 p-6">
			{@render children()}
		</main>
	</div>
{/if}

<GlobalDialog />
{#if data.user}
	<!-- Signed-in only: everything it offers needs an account. -->
	<CommandPalette />
{/if}
