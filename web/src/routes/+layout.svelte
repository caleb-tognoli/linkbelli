<script lang="ts">
	import '../app.css';
	import { page } from '$app/state';
	import type { LayoutData } from './$types';

	let { data, children }: { data: LayoutData; children: import('svelte').Snippet } = $props();

	const onHome = $derived(page.url.pathname === '/');
	const onDiscover = $derived(page.url.pathname.startsWith('/discover'));
	const onSettings = $derived(page.url.pathname.startsWith('/settings'));
	// Anonymous auth pages (login/register) get centered card chrome; other anonymous pages
	// (public playlist view, discover) get a normal top-aligned container with a brand bar.
	const isAuthPage = $derived(['/login', '/register'].includes(page.url.pathname));
</script>

{#if data.user}
	<div class="flex min-h-screen">
		<aside
			class="flex w-56 shrink-0 flex-col gap-1 border-r p-4"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="px-2 pb-4 text-lg font-semibold">Linkbelli</div>

			<nav class="flex flex-col gap-0.5 text-sm">
				<a
					href="/"
					class="rounded px-2 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={onHome}
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
					aria-current={onDiscover ? 'page' : undefined}
				>
					Discover
				</a>
				<a
					href="/settings"
					class="rounded px-2 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
					class:font-medium={onSettings}
					aria-current={onSettings ? 'page' : undefined}
				>
					Settings
				</a>
			</nav>

			<div class="mt-auto border-t pt-3 text-sm" style="border-color: var(--color-border)">
				<div class="px-2 pb-2 truncate" style="color: var(--color-muted)" title={data.user.userId}>
					{data.user.userId}
				</div>
				<form method="post" action="/logout">
					<button
						type="submit"
						class="w-full rounded px-2 py-1.5 text-left hover:bg-black/5 dark:hover:bg-white/10"
					>
						Sign out
					</button>
				</form>
			</div>
		</aside>

		<main class="flex-1 p-8">
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
