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
	<!-- The frame shows the host page behind the card's rounded-control corners, not the app's own
	     background, which is only there to be a page. -->
	<style>
		html,
		body {
			background: transparent;
		}
	</style>
</svelte:head>

<!-- Deliberately self-contained: an embed inherits nothing from the host page, and must not
     depend on the app's own chrome, which is not rendered here. -->
<div class="embed" data-embed-theme={data.theme}>
	<header>
		<a href={playlistUrl} target="_blank" rel="noopener noreferrer" class="title">
			{data.playlist.name}<span class="sr-only"> (opens in a new tab)</span></a>
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
						{item.metadata?.title ?? item.link.title ?? item.link.url}<span class="sr-only"> (opens in a new tab)</span></a>
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
	/* Driven by its own attribute rather than the app's theme cookie: the host page decides which
	   one fits, and has no way to set our cookie. The colours are the app's own tokens, each
	   written once as light-dark(); setting color-scheme here picks which half applies inside
	   the card, whatever the page around it is using. */
	.embed {
		color-scheme: light;

		box-sizing: border-box;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		padding: 0.9rem;
		border: 1px solid var(--color-border);
		border-radius: 8px;
		background: var(--color-bg);
		color: var(--color-text);
		font:
			14px/1.5 ui-sans-serif,
			system-ui,
			-apple-system,
			'Segoe UI',
			Roboto,
			sans-serif;
	}

	.embed[data-embed-theme='dark'] {
		color-scheme: dark;
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
		color: var(--color-muted);
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
		border-top: 1px solid var(--color-border);
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
		background: var(--color-border);
	}

	.host {
		flex: none;
	}

	footer a {
		display: inline-flex;
		align-items: center;
		gap: 0.25rem;
		color: var(--color-accent);
		font-size: 12px;
		text-decoration: none;
	}

	footer a:hover {
		text-decoration: underline;
	}
</style>
