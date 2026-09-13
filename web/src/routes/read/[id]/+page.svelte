<script lang="ts">
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { readingMinutes } from '$lib/reading';
	import {
		FONTS,
		FONT_CSS,
		SIZES,
		SIZE_CSS,
		WIDTHS,
		WIDTH_CSS,
		readerSettings
	} from '$lib/readerSettings.svelte';
	import { ChevronLeft, ChevronRight, Check, ExternalLink, Settings2 } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const minutes = $derived(readingMinutes(data.content.wordCount));

	let article = $state<HTMLElement | null>(null);
	let progress = $state(data.content.readProgress ?? 0);
	let finished = $state((data.content.readProgress ?? 0) >= 0.92);
	let showSettings = $state(false);

	/** Matches LinkService.FinishedAt. Every article ends in a footer nobody reads. */
	const FINISHED_AT = 0.92;

	/**
	 * How long to wait after scrolling stops before telling the server.
	 *
	 * Long enough that a flick down the page is one request rather than forty, short enough that
	 * closing the tab shortly after reading does not lose where you were.
	 */
	const SAVE_AFTER_MS = 1500;

	let saveTimer: ReturnType<typeof setTimeout> | null = null;
	let lastSaved = data.content.readProgress ?? 0;

	/**
	 * How far down the article the bottom of the window has reached.
	 *
	 * Measured against the article element rather than the document, because the header, the
	 * settings row and the footer are not the article and counting them would report somebody as
	 * finished while a paragraph was still on screen.
	 */
	function measure(): number {
		if (!article) return 0;

		const top = article.offsetTop;
		const height = article.offsetHeight;
		if (height <= 0) return 0;

		// Everything visible counts as read: if the whole article fits on the screen, opening it
		// is reading it.
		const seen = window.scrollY + window.innerHeight - top;
		return Math.min(1, Math.max(0, seen / height));
	}

	function onScroll() {
		const now = measure();
		if (now > progress) progress = now;
		if (progress >= FINISHED_AT) finished = true;

		if (saveTimer) clearTimeout(saveTimer);
		saveTimer = setTimeout(save, SAVE_AFTER_MS);
	}

	async function save() {
		// A fifth of the article is the smallest move worth a request. Without this, a page that
		// reflows on an image load would post a new position for a pixel.
		if (progress - lastSaved < 0.02 && progress < 1) return;

		lastSaved = progress;
		await api.put(`/links/${data.content.id}/progress`, { progress });
	}

	async function markFinished() {
		progress = 1;
		finished = true;
		lastSaved = 1;
		await api.put(`/links/${data.content.id}/progress`, { progress: 1 });
	}

	function open(linkId: string) {
		const suffix = data.from ? `?from=${encodeURIComponent(data.from)}` : '';
		void goto(`/read/${linkId}${suffix}`);
	}

	function back() {
		void goto(data.from ? `/playlists/${data.from}` : '/queue');
	}

	/**
	 * The keyboard vocabulary the rest of the app already has.
	 *
	 * LinkTable ships a whole keyboard model and advertises it in a footer; the reader — the
	 * screen somebody working through a queue actually lives on — had none of it.
	 */
	function onKeydown(event: KeyboardEvent) {
		const target = event.target as HTMLElement | null;
		if (event.metaKey || event.ctrlKey || event.altKey) return;
		if (target?.tagName === 'INPUT' || target?.tagName === 'TEXTAREA' || target?.isContentEditable)
			return;

		switch (event.key) {
			case 'j':
				window.scrollBy({ top: window.innerHeight * 0.4, behavior: 'smooth' });
				break;
			case 'k':
				window.scrollBy({ top: -window.innerHeight * 0.4, behavior: 'smooth' });
				break;
			case 'n':
				if (data.next) open(data.next.linkId);
				break;
			case 'p':
				if (data.previous) open(data.previous.linkId);
				break;
			case 'e':
				void markFinished();
				break;
			case 'Escape':
				back();
				break;
			default:
				return;
		}

		event.preventDefault();
	}

	onMount(() => {
		readerSettings.load();

		// Where the reading stopped last time. Done after mount so the layout is settled — the
		// same offset against an unlaid-out article lands somewhere else entirely.
		if (article && progress > 0 && progress < FINISHED_AT) {
			const to = article.offsetTop + article.offsetHeight * progress - window.innerHeight * 0.5;
			window.scrollTo({ top: Math.max(0, to) });
		}

		window.addEventListener('scroll', onScroll, { passive: true });
		window.addEventListener('keydown', onKeydown);

		return () => {
			window.removeEventListener('scroll', onScroll);
			window.removeEventListener('keydown', onKeydown);
			if (saveTimer) clearTimeout(saveTimer);
			// Whatever is unsaved goes now: closing the tab is the most likely way to leave.
			void save();
		};
	});
</script>

<svelte:head><title>{data.content.title ?? 'Reading'} - linkbelli</title></svelte:head>

<!-- Where you are, pinned to the top. The one thing a long article never told you. -->
<div
	class="fixed inset-x-0 top-0 z-20 h-0.5"
	style="background: var(--color-accent); width: {Math.round(progress * 100)}%"
	role="progressbar"
	aria-label="How far through this article you are"
	aria-valuenow={Math.round(progress * 100)}
	aria-valuemin="0"
	aria-valuemax="100"
></div>

<article
	bind:this={article}
	class="mx-auto pb-16"
	style="max-width: {WIDTH_CSS[readerSettings.width]}"
>
	<header class="border-b pb-4" style="border-color: var(--color-border)">
		<h1 class="text-2xl font-semibold leading-tight">{data.content.title ?? data.content.url}</h1>
		<p class="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm" style="color: var(--color-muted)">
			<span>{data.content.siteName ?? data.content.host}</span>
			<span aria-hidden="true">·</span>
			<span>{data.content.wordCount.toLocaleString()} words</span>
			{#if minutes}
				<span aria-hidden="true">·</span>
				<span>{minutes} min read</span>
			{/if}
			<span aria-hidden="true">·</span>
			<a
				href={data.content.url}
				target="_blank"
				rel="noreferrer"
				class="inline-flex items-center gap-1 hover:underline"
			>
				Original
				<ExternalLink size={12} aria-hidden="true" />
			</a>
		</p>

		<div class="mt-3 flex flex-wrap items-center gap-2">
			<button
				type="button"
				onclick={() => (showSettings = !showSettings)}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm"
				style="border-color: var(--color-border)"
				aria-expanded={showSettings}
			>
				<Settings2 size={14} aria-hidden="true" /> How it reads
			</button>

			<button
				type="button"
				onclick={markFinished}
				disabled={finished}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm disabled:opacity-60"
				style="border-color: var(--color-border)"
				title="Mark finished (e)"
			>
				<Check size={14} aria-hidden="true" />
				{finished ? 'Finished' : 'Mark finished'}
			</button>

			{#if data.previous}
				<button
					type="button"
					onclick={() => open(data.previous!.linkId)}
					class="inline-flex items-center gap-1 rounded-md border px-2.5 py-1.5 text-sm"
					style="border-color: var(--color-border)"
					title="Previous in this playlist (p)"
				>
					<ChevronLeft size={14} aria-hidden="true" /> Previous
				</button>
			{/if}
			{#if data.next}
				<button
					type="button"
					onclick={() => open(data.next!.linkId)}
					class="inline-flex items-center gap-1 rounded-md border px-2.5 py-1.5 text-sm"
					style="border-color: var(--color-border)"
					title="Next in this playlist (n)"
				>
					Next <ChevronRight size={14} aria-hidden="true" />
				</button>
			{/if}
		</div>

		{#if showSettings}
			<div class="mt-3 flex flex-col gap-2 rounded-md border p-3 text-sm" style="border-color: var(--color-border)">
				<!-- Per device, on purpose: the right size on a phone is the wrong one on a desktop. -->
				<div class="flex flex-wrap items-center gap-2">
					<span class="w-16 shrink-0" style="color: var(--color-muted)">Size</span>
					{#each SIZES as size (size)}
						<button
							type="button"
							onclick={() => readerSettings.set('size', size)}
							class="rounded border px-2 py-1 capitalize"
							class:font-medium={readerSettings.size === size}
							style="border-color: {readerSettings.size === size
								? 'var(--color-accent)'
								: 'var(--color-border)'}"
						>{size}</button>
					{/each}
				</div>
				<div class="flex flex-wrap items-center gap-2">
					<span class="w-16 shrink-0" style="color: var(--color-muted)">Width</span>
					{#each WIDTHS as width (width)}
						<button
							type="button"
							onclick={() => readerSettings.set('width', width)}
							class="rounded border px-2 py-1 capitalize"
							class:font-medium={readerSettings.width === width}
							style="border-color: {readerSettings.width === width
								? 'var(--color-accent)'
								: 'var(--color-border)'}"
						>{width}</button>
					{/each}
				</div>
				<div class="flex flex-wrap items-center gap-2">
					<span class="w-16 shrink-0" style="color: var(--color-muted)">Face</span>
					{#each FONTS as font (font)}
						<button
							type="button"
							onclick={() => readerSettings.set('font', font)}
							class="rounded border px-2 py-1 capitalize"
							class:font-medium={readerSettings.font === font}
							style="border-color: {readerSettings.font === font
								? 'var(--color-accent)'
								: 'var(--color-border)'}; font-family: {FONT_CSS[font]}"
						>{font}</button>
					{/each}
				</div>
				<p class="text-xs" style="color: var(--color-muted)">
					Kept on this device. <kbd>j</kbd>/<kbd>k</kbd> to move, <kbd>n</kbd>/<kbd>p</kbd> for the
					next and previous in the playlist, <kbd>e</kbd> to mark finished, <kbd>Esc</kbd> to go back.
				</p>
			</div>
		{/if}
	</header>

	<!-- Text only, and deliberately so: this is the copy saved at the time, not a rendering of
	     the page. Images and embeds still live on the site, which is the part that rots. -->
	<div
		class="mt-6 flex flex-col gap-4 leading-relaxed"
		style="font-size: {SIZE_CSS[readerSettings.size]}; font-family: {FONT_CSS[readerSettings.font]}"
	>
		{#each data.content.paragraphs as paragraph, index (index)}
			<p>{paragraph}</p>
		{/each}
	</div>

	{#if data.content.truncated}
		<p class="mt-6 border-t pt-4 text-sm" style="border-color: var(--color-border); color: var(--color-muted)">
			This article was longer than linkbelli keeps. The rest is still at the
			<a href={data.content.url} target="_blank" rel="noreferrer" class="underline">original</a>.
		</p>
	{/if}
</article>
