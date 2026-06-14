<script lang="ts">
	import '../app.css';
	import { page } from '$app/state';
	import type { LayoutData } from './$types';

	let { data, children }: { data: LayoutData; children: import('svelte').Snippet } = $props();

	const nav = [
		{ href: '/', label: 'Home', enabled: true },
		{ href: '/discover', label: 'Discover', enabled: false },
		{ href: '/playlists', label: 'Playlists', enabled: false },
		{ href: '/settings', label: 'Settings', enabled: false }
	];
</script>

{#if data.user}
	<div class="flex min-h-screen">
		<aside
			class="flex w-56 shrink-0 flex-col gap-1 border-r p-4"
			style="border-color: var(--color-border); background: var(--color-surface)"
		>
			<div class="px-2 pb-4 text-lg font-semibold">Linkbelli</div>

			<nav class="flex flex-col gap-0.5 text-sm">
				{#each nav as item (item.href)}
					{#if item.enabled}
						<a
							href={item.href}
							class="rounded px-2 py-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							class:font-medium={page.url.pathname === item.href}
							aria-current={page.url.pathname === item.href ? 'page' : undefined}
						>
							{item.label}
						</a>
					{:else}
						<span
							class="cursor-default rounded px-2 py-1.5"
							style="color: var(--color-muted)"
							title="Coming soon"
						>
							{item.label}
						</span>
					{/if}
				{/each}
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
{:else}
	<main class="flex min-h-screen items-center justify-center p-6">
		{@render children()}
	</main>
{/if}
