<script lang="ts">
	import '../app.css';
	import { Dialog } from 'bits-ui';
	import { LogOut, Menu } from '@lucide/svelte';
	import { page } from '$app/state';
	import { afterNavigate } from '$app/navigation';
	import type { LayoutData } from './$types';

	let { data, children }: { data: LayoutData; children: import('svelte').Snippet } = $props();

	const onHome = $derived(page.url.pathname === '/');
	const onDiscover = $derived(page.url.pathname.startsWith('/discover'));
	const onSettings = $derived(page.url.pathname.startsWith('/settings'));
	// Anonymous auth pages (login/register) get centered card chrome; other anonymous pages
	// (public playlist view, discover) get a normal top-aligned container with a brand bar.
	const isAuthPage = $derived(['/login', '/register'].includes(page.url.pathname));

	// Mobile nav drawer (below md the sidebar is hidden). Close it after navigating.
	let drawerOpen = $state(false);
	afterNavigate(() => (drawerOpen = false));
</script>

<!-- Shared nav body — rendered in both the desktop sidebar and the mobile drawer. -->
{#snippet navBody()}
	<nav class="flex flex-col gap-0.5 text-sm">
		<a
			href="/"
			class="rounded px-2 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
			class:font-medium={onHome}
			style={onHome ? 'background: var(--color-border)' : ''}
			aria-current={onHome ? 'page' : undefined}
		>
			Home
		</a>
		<!-- Home subsections: scroll to the matching section. -->
		<a href="/#playlists" class="rounded px-2 py-1.5 pl-5 hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-muted)">
			Playlists
		</a>
		<a href="/#sources" class="rounded px-2 py-1.5 pl-5 hover:bg-black/5 dark:hover:bg-white/10" style="color: var(--color-muted)">
			Sources
		</a>

		<a
			href="/discover"
			class="rounded px-2 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
			class:font-medium={onDiscover}
			style={onDiscover ? 'background: var(--color-border)' : ''}
			aria-current={onDiscover ? 'page' : undefined}
		>
			Discover
		</a>
		<a
			href="/settings"
			class="rounded px-2 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
			class:font-medium={onSettings}
			style={onSettings ? 'background: var(--color-border)' : ''}
			aria-current={onSettings ? 'page' : undefined}
		>
			Settings
		</a>
	</nav>

	<div class="mt-auto border-t pt-3 text-sm" style="border-color: var(--color-border)">
		<div class="px-2 pb-2 truncate" style="color: var(--color-muted)" title={data.user?.userId}>
			{data.user?.userId}
		</div>
		<form method="post" action="/logout">
			<button
				type="submit"
				class="flex w-full items-center justify-center rounded p-2 hover:bg-black/5 dark:hover:bg-white/10"
				title="Sign out"
				aria-label="Sign out"
			>
				<LogOut size={16} aria-hidden="true" />
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
					<Menu size={20} aria-hidden="true" />
				</Dialog.Trigger>
				<Dialog.Portal>
					<Dialog.Overlay class="fixed inset-0 z-40 bg-black/40" />
					<Dialog.Content
						class="fixed inset-y-0 left-0 z-50 flex w-64 flex-col gap-1 border-r p-4"
						style="border-color: var(--color-border); background: var(--color-surface)"
					>
						<Dialog.Title class="px-2 pb-4 text-lg font-semibold">Linkbelli</Dialog.Title>
						{@render navBody()}
					</Dialog.Content>
				</Dialog.Portal>
			</Dialog.Root>
			<span class="text-lg font-semibold">Linkbelli</span>
		</header>

		<aside
			class="hidden w-56 shrink-0 flex-col gap-1 border-r p-4 md:flex"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="px-2 pb-4 text-lg font-semibold">Linkbelli</div>
			{@render navBody()}
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
			<a href="/discover" class="text-lg font-semibold">Linkbelli</a>
			<a href="/login" class="text-sm font-medium" style="color: var(--color-accent)">Sign in</a>
		</header>
		<main class="mx-auto w-full max-w-5xl flex-1 p-6">
			{@render children()}
		</main>
	</div>
{/if}
