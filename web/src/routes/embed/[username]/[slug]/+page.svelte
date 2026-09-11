<script lang="ts">
	import { page } from '$app/state';
	import { ExternalLink } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const playlistUrl = $derived(
		`${page.url.origin}/public/${encodeURIComponent(data.username)}/${encodeURIComponent(data.slug)}`
	);
</script>

<svelte:head>
	<title>{data.playlist.name}</title>
	<!-- An embed is a fragment of someone else's page; it should never be indexed as one of ours. -->
	<meta name="robots" content="noindex" />
</svelte:head>

<!-- Deliberately self-contained: an embed inherits nothing from the host page, and must not
     depend on the app's own chrome, which is not rendered here. -->
<div class="embed" data-embed-theme={data.theme}>
	<header>
		<a href={playlistUrl} target="_blank" rel="noopener noreferrer" class="title">
			{data.playlist.name}
		</a>
		<span class="by">by @{data.username}</span>
	</header>

	{#if data.playlist.description}
		<p class="description">{data.playlist.description}</p>
	{/if}

	{#if data.items.items.length === 0}
		<p class="empty">Nothing in this playlist yet.</p>
	{:else}
		<ul>
			{#each data.items.items as item (item.id)}
				<li>
					{#if item.link.favicon}
						<img src={item.link.favicon} alt="" class="icon" loading="lazy" />
					{:else}
						<span class="icon placeholder"></span>
					{/if}
					<a href={item.link.url} target="_blank" rel="noopener noreferrer">
						{item.metadata?.title ?? item.link.title ?? item.link.url}
					</a>
					<span class="host">{item.link.host}</span>
				</li>
			{/each}
		</ul>
	{/if}

	<footer>
		<a href={playlistUrl} target="_blank" rel="noopener noreferrer">
			{#if data.playlist.itemCount > data.items.items.length}
				All {data.playlist.itemCount} links
			{:else}
				Open in Linkbelli
			{/if}
			<ExternalLink size={11} aria-hidden="true" />
		</a>
	</footer>
</div>

<style>
	/* Scoped to the embed and driven by its own attribute rather than the app's theme cookie:
	   the host page decides which one fits, and has no way to set our cookie. */
	.embed {
		--embed-bg: #ffffff;
		--embed-surface: #f7f7f5;
		--embed-border: #e8e8e6;
		--embed-text: #1f1f1f;
		--embed-muted: #6b6b6b;
		--embed-accent: #2563eb;

		box-sizing: border-box;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		padding: 0.9rem;
		border: 1px solid var(--embed-border);
		border-radius: 8px;
		background: var(--embed-bg);
		color: var(--embed-text);
		font:
			14px/1.5 ui-sans-serif,
			system-ui,
			-apple-system,
			'Segoe UI',
			Roboto,
			sans-serif;
	}

	.embed[data-embed-theme='dark'] {
		--embed-bg: #191919;
		--embed-surface: #202020;
		--embed-border: #2f2f2f;
		--embed-text: #ededed;
		--embed-muted: #9a9a9a;
		--embed-accent: #3b82f6;
	}

	header {
		display: flex;
		flex-wrap: wrap;
		align-items: baseline;
		gap: 0.5rem;
	}

	.title {
		font-weight: 600;
		color: inherit;
		text-decoration: none;
	}

	.title:hover {
		text-decoration: underline;
	}

	.by,
	.host,
	.description,
	.empty {
		color: var(--embed-muted);
		font-size: 12px;
	}

	.description {
		margin: 0;
	}

	ul {
		display: flex;
		flex-direction: column;
		margin: 0;
		padding: 0;
		list-style: none;
	}

	li {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.35rem 0;
		border-top: 1px solid var(--embed-border);
		min-width: 0;
	}

	li:first-child {
		border-top: 0;
	}

	li a {
		flex: 1;
		min-width: 0;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		color: inherit;
		text-decoration: none;
	}

	li a:hover {
		text-decoration: underline;
	}

	.icon {
		flex: none;
		width: 14px;
		height: 14px;
		object-fit: contain;
	}

	.placeholder {
		border-radius: 2px;
		background: var(--embed-border);
	}

	.host {
		flex: none;
	}

	footer a {
		display: inline-flex;
		align-items: center;
		gap: 0.25rem;
		color: var(--embed-accent);
		font-size: 12px;
		text-decoration: none;
	}

	footer a:hover {
		text-decoration: underline;
	}
</style>
