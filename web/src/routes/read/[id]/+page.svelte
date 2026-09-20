<script lang="ts">
	import BackLink from '$lib/components/ui/BackLink.svelte';
	import { toast } from '$lib/toast.svelte';
	import SegmentedControl from '$lib/components/ui/SegmentedControl.svelte';
	import Textarea from '$lib/components/ui/Textarea.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { afterNavigate, beforeNavigate, goto } from '$app/navigation';
	import { onMount, tick } from 'svelte';
	import { api } from '$lib/api/client';
	import { confirmDialog } from '$lib/dialog.svelte';
	import { offsetIn, tidy, toSegments, type Highlight } from '$lib/highlights';
	import { readingMinutes } from '$lib/reading';
	import { scrollBehavior } from '$lib/motion';
	import {
		FONTS,
		FONT_CSS,
		SIZES,
		SIZE_CSS,
		WIDTHS,
		WIDTH_CSS,
		readerSettings
	} from '$lib/readerSettings.svelte';
	import {
		Check,
		ChevronLeft,
		ChevronRight,
		ExternalLink,
		Highlighter,
		MessageSquarePlus,
		Pencil,
		Settings2,
		Trash2,
		X
	} from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const minutes = $derived(readingMinutes(data.content.wordCount));

	/** Matches LinkService.FinishedAt. Every article ends in a footer nobody reads. */
	const FINISHED_AT = 0.92;

	/**
	 * How long to wait after scrolling stops before telling the server.
	 *
	 * Long enough that a flick down the page is one request rather than forty, short enough that
	 * closing the tab shortly after reading does not lose where you were.
	 */
	const SAVE_AFTER_MS = 1500;

	/** How long a selection has to sit still before the highlight button is offered for it. */
	const SETTLE_MS = 250;

	// Everything below belongs to one article. SvelteKit keeps this component when n/p moves to
	// the next one and only swaps `data`, so these are set in afterNavigate rather than trusted to
	// their initial values — which is also the difference between saving where you got to in
	// this article and writing it onto the next one.
	let article = $state<HTMLElement | null>(null);
	let body = $state<HTMLElement | null>(null);
	let progress = $state(0);
	let finished = $state(false);
	let showSettings = $state(false);
	let highlights = $state<Highlight[]>([]);

	let saveTimer: ReturnType<typeof setTimeout> | null = null;
	let lastSaved = 0;

	/** A selection that could become a highlight, and where to offer it. */
	/**
	 * Whether this is being read with a finger.
	 *
	 * Decided by the pointer rather than the width: a tablet is wide and still has the same
	 * native selection callout to stay out of the way of.
	 */
	let touch = $state(false);
	$effect(() => {
		const coarse = window.matchMedia('(pointer: coarse)');
		touch = coarse.matches;
		const onchange = (event: MediaQueryListEvent) => (touch = event.matches);
		coarse.addEventListener('change', onchange);
		return () => coarse.removeEventListener('change', onchange);
	});

	let selection = $state<{
		paragraphIndex: number;
		start: number;
		end: number;
		range: Range;
		/** Dragged across more than one paragraph, which a highlight cannot be. */
		spans: boolean;
	} | null>(null);
	let settleTimer: ReturnType<typeof setTimeout> | null = null;

	/** The highlight whose note is being looked at, and what to hang the panel off. */
	let active = $state<{ id: string; anchor: Element } | null>(null);
	let draft = $state('');

	// Bumped on scroll and resize so the floating panels follow what they are attached to.
	let viewport = $state(0);

	const byParagraph = $derived.by(() => {
		const map = new Map<number, Highlight[]>();
		for (const h of highlights) {
			const list = map.get(h.paragraphIndex) ?? [];
			list.push(h);
			map.set(h.paragraphIndex, list);
		}
		return map;
	});

	const noted = $derived(new Set(highlights.filter((h) => h.note).map((h) => h.id)));
	const activeHighlight = $derived(highlights.find((h) => h.id === active?.id) ?? null);
	const ordered = $derived(
		[...highlights].sort((a, b) => a.paragraphIndex - b.paragraphIndex || a.start - b.start)
	);

	const toolbarAt = $derived.by(() => {
		void viewport;
		if (!selection) return null;
		const rect = selection.range.getBoundingClientRect();
		return { x: rect.left + rect.width / 2, y: rect.top };
	});

	const panelAt = $derived.by(() => {
		void viewport;
		if (!active) return null;
		const rect = active.anchor.getBoundingClientRect();
		const width = Math.min(320, window.innerWidth - 16);
		return {
			x: Math.max(8, Math.min(rect.left, window.innerWidth - width - 8)),
			y: rect.bottom + 8,
			width
		};
	});

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
		viewport++;

		const now = measure();
		if (now > progress) progress = now;
		if (progress >= FINISHED_AT) finished = true;

		if (saveTimer) clearTimeout(saveTimer);
		saveTimer = setTimeout(save, SAVE_AFTER_MS);
	}

	async function save() {
		// A fiftieth of the article is the smallest move worth a request. Without this, a page
		// that reflows on an image load would post a new position for a pixel.
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

	const backHref = $derived(data.from ? `/playlists/${data.from}` : '/queue');
	const backLabel = $derived(data.from ? (data.fromName ?? 'Back to the playlist') : 'Up next');

	function back() {
		void goto(backHref);
	}

	/** The paragraph a DOM node sits in, if it is in one. */
	function paragraphOf(node: Node): HTMLElement | null {
		const element = node.nodeType === Node.ELEMENT_NODE ? (node as Element) : node.parentElement;
		return element?.closest<HTMLElement>('[data-paragraph]') ?? null;
	}

	/**
	 * Turns whatever is selected into something that could be highlighted.
	 *
	 * Offsets are worked out against the paragraph's own text rather than taken from the DOM,
	 * because once anything in a paragraph is marked it is no longer one text node, and the
	 * browser's offsets are relative to whichever run the selection happened to land in.
	 */
	function readSelection() {
		const current = window.getSelection();
		if (!current || current.isCollapsed || current.rangeCount === 0 || !body) {
			selection = null;
			return;
		}

		const range = current.getRangeAt(0);
		if (!body.contains(range.commonAncestorContainer)) {
			selection = null;
			return;
		}

		const from = paragraphOf(range.startContainer);
		const to = paragraphOf(range.endContainer);
		if (!from || !to) {
			selection = null;
			return;
		}

		if (from !== to) {
			selection = { paragraphIndex: -1, start: 0, end: 0, range, spans: true };
			return;
		}

		const index = Number(from.dataset.paragraph);
		const start = offsetIn(from, range.startContainer, range.startOffset);
		const end = offsetIn(from, range.endContainer, range.endOffset);
		if (start === null || end === null) {
			selection = null;
			return;
		}

		const trimmed = tidy(data.content.paragraphs[index], start, end);
		selection =
			trimmed.start < trimmed.end
				? { paragraphIndex: index, ...trimmed, range, spans: false }
				: null;
	}

	/**
	 * Waits for a selection to stop moving before offering anything for it.
	 *
	 * `selectionchange` fires on every character of a drag, and a button that chases the cursor
	 * is worse than one that turns up a moment after you let go. It is also the one event that
	 * covers a mouse, a keyboard and a phone's selection handles alike.
	 */
	function onSelectionChange() {
		if (settleTimer) clearTimeout(settleTimer);
		settleTimer = setTimeout(readSelection, SETTLE_MS);
	}

	async function highlight(withNote: boolean) {
		if (!selection || selection.spans) return;

		const { paragraphIndex, start, end } = selection;
		selection = null;
		window.getSelection()?.removeAllRanges();

		const res = await api.post(`/links/${data.content.id}/highlights`, {
			paragraphIndex,
			start,
			end,
			text: data.content.paragraphs[paragraphIndex].slice(start, end)
		});

		if (!res.ok) {
			complain(
				res.status === 400
					? ((await res.json().catch(() => null))?.detail ?? 'That could not be highlighted.')
					: 'That could not be highlighted just now.'
			);
			return;
		}

		const created = (await res.json()) as Highlight;
		highlights = [...highlights.filter((h) => h.id !== created.id), created];

		if (withNote) {
			await tick();
			const mark = body?.querySelector(`[data-highlight="${created.id}"]`);
			if (mark) openPanel(created.id, mark);
		}
	}

	function openPanel(id: string, anchor: Element) {
		active = { id, anchor };
		draft = highlights.find((h) => h.id === id)?.note ?? '';
		selection = null;
	}

	function closePanel() {
		active = null;
		draft = '';
	}

	async function saveNote() {
		if (!active) return;

		const id = active.id;
		const res = await api.patch(`/highlights/${id}`, { note: draft });
		if (!res.ok) {
			complain('That note could not be saved.');
			return;
		}

		const updated = (await res.json()) as Highlight;
		highlights = highlights.map((h) => (h.id === id ? updated : h));
		closePanel();
	}

	async function remove(target: Highlight) {
		// Asked only when there is a note to lose. Unmarking a passage is a click to undo; a
		// paragraph of your own thinking is not.
		if (
			target.note &&
			!(await confirmDialog('Remove this highlight and its note?', {
				danger: true,
				confirmLabel: 'Remove'
			}))
		) {
			return;
		}

		const res = await api.del(`/highlights/${target.id}`);
		if (!res.ok) {
			complain('That highlight could not be removed.');
			return;
		}

		highlights = highlights.filter((h) => h.id !== target.id);
		if (active?.id === target.id) closePanel();
	}

	function jumpTo(target: Highlight) {
		const mark = body?.querySelector(`[data-highlight="${target.id}"]`);
		mark?.scrollIntoView({ behavior: scrollBehavior(), block: 'center' });
	}

	const complain = (message: string) => toast.error(message);

	/**
	 * A click on marked text opens its note.
	 *
	 * Listened for on the article rather than on each mark: a mark is a run of words, not a
	 * control, and making every one of them a tab stop would put fifty stops between somebody
	 * and the end of a heavily marked article. The list of marked passages under the article is
	 * the keyboard route to the same thing.
	 */
	function onBodyClick(event: MouseEvent) {
		// The end of a drag is a click too, and that one is making a selection, not opening one.
		if (!window.getSelection()?.isCollapsed) return;

		const mark = (event.target as Element | null)?.closest<HTMLElement>('[data-highlight]');
		if (mark?.dataset.highlight) openPanel(mark.dataset.highlight, mark);
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
				window.scrollBy({ top: window.innerHeight * 0.4, behavior: scrollBehavior() });
				break;
			case 'k':
				window.scrollBy({ top: -window.innerHeight * 0.4, behavior: scrollBehavior() });
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
			case 'h':
				// Read now rather than waiting out the settle delay: somebody who selects and
				// presses h straight away has already decided.
				readSelection();
				if (!selection || selection.spans) return;
				void highlight(false);
				break;
			case 'Escape':
				// Closes whatever is open before it leaves the article — and leaves only when
				// nothing has focus, so a second press to dismiss something already gone does not
				// throw away the page.
				if (active) closePanel();
				else if (selection) {
					selection = null;
					window.getSelection()?.removeAllRanges();
				} else if (document.activeElement === document.body || document.activeElement === null) back();
				else (document.activeElement as HTMLElement).blur();
				break;
			default:
				return;
		}

		event.preventDefault();
	}

	/** Puts the page back where the reading stopped last time. */
	function resume() {
		if (article && progress > 0 && progress < FINISHED_AT) {
			const to = article.offsetTop + article.offsetHeight * progress - window.innerHeight * 0.5;
			window.scrollTo({ top: Math.max(0, to) });
		}
	}

	// Whatever is unsaved goes before leaving, while data still names the article it belongs to.
	beforeNavigate(() => {
		if (saveTimer) clearTimeout(saveTimer);
		saveTimer = null;
		void save();
	});

	afterNavigate(() => {
		progress = data.content.readProgress ?? 0;
		lastSaved = progress;
		finished = progress >= FINISHED_AT;
		highlights = data.highlights;
		selection = null;
		closePanel();

		// After the layout has settled — the same offset against an unlaid-out article lands
		// somewhere else entirely.
		void tick().then(resume);
	});

	onMount(() => {
		readerSettings.load();

		const bump = () => viewport++;

		window.addEventListener('scroll', onScroll, { passive: true });
		window.addEventListener('resize', bump, { passive: true });
		window.addEventListener('keydown', onKeydown);
		document.addEventListener('selectionchange', onSelectionChange);

		return () => {
			window.removeEventListener('scroll', onScroll);
			window.removeEventListener('resize', bump);
			window.removeEventListener('keydown', onKeydown);
			document.removeEventListener('selectionchange', onSelectionChange);
			if (saveTimer) clearTimeout(saveTimer);
			if (settleTimer) clearTimeout(settleTimer);
			// Closing the tab is the most likely way to leave, and it does not navigate.
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
	<!-- A way out that does not need a keyboard: on a phone the browser's own back button was the
	     only one. -->
	<BackLink href={backHref} label={backLabel} class="mb-3" />
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
			{#if highlights.length}
				<span aria-hidden="true">·</span>
				<a href="#marked" class="hover:underline">
					{highlights.length} marked
				</a>
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
				<Settings2 size={14} aria-hidden="true" /> Text settings
			</button>

			<button
				type="button"
				onclick={markFinished}
				disabled={finished}
				class="inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-sm disabled:opacity-60"
				style="border-color: var(--color-border)"
				title="Mark done (e)"
			>
				<Check size={14} aria-hidden="true" />
				{finished ? 'Done' : 'Mark done'}
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
				{#each [
					{ key: 'size', label: 'Size', values: SIZES },
					{ key: 'width', label: 'Width', values: WIDTHS },
					{ key: 'font', label: 'Face', values: FONTS }
				] as const as setting (setting.key)}
					<div class="flex flex-wrap items-center gap-2">
						<span class="w-16 shrink-0" style="color: var(--color-muted)">{setting.label}</span>
						<SegmentedControl
							label={setting.label}
							size="sm"
							options={setting.values.map((value) => ({
								value,
								label: value.charAt(0).toUpperCase() + value.slice(1)
							}))}
							value={readerSettings[setting.key]}
							onchange={(value) => readerSettings.set(setting.key, value as never)}
						/>
					</div>
				{/each}
				<p class="text-xs" style="color: var(--color-muted)">
					Kept on this device. <kbd>j</kbd>/<kbd>k</kbd> to move, <kbd>n</kbd>/<kbd>p</kbd> for the
					next and previous in the playlist, <kbd>e</kbd> to mark finished, <kbd>h</kbd> to
					highlight what is selected, <kbd>Esc</kbd> to go back (once nothing else is open).
				</p>
			</div>
		{/if}
	</header>

	<!-- Text only, and deliberately so: this is the copy saved at the time, not a rendering of
	     the page. Images and embeds still live on the site, which is the part that rots.

	     The click listener opens a mark's note; the list under the article is the keyboard route
	     to the same thing, so the marks themselves are not made into fifty tab stops. -->
	<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
	<div
		bind:this={body}
		onclick={onBodyClick}
		class="mt-6 flex flex-col gap-4 leading-relaxed"
		style="font-size: {SIZE_CSS[readerSettings.size]}; font-family: {FONT_CSS[readerSettings.font]}"
	>
		{#each data.content.paragraphs as paragraph, index (index)}
			<!-- One line on purpose: whitespace between these blocks would be read as the article's. -->
			<p data-paragraph={index}>{#each toSegments(paragraph, byParagraph.get(index) ?? []) as segment, s (s)}{#if segment.ids.length}<mark
							data-highlight={segment.ids[segment.ids.length - 1]}
							class="cursor-pointer rounded-sm"
							class:noted={segment.ids.some((id) => noted.has(id))}
							style="color: inherit; background: {segment.ids.length > 1 ||
							segment.ids.includes(active?.id ?? '')
								? 'var(--color-highlight-strong)'
								: 'var(--color-highlight)'}">{segment.text}</mark
						>{:else}{segment.text}{/if}{/each}</p>
		{/each}
	</div>

	{#if data.content.truncated}
		<p class="mt-6 border-t pt-4 text-sm" style="border-color: var(--color-border); color: var(--color-muted)">
			This article was longer than linkbelli keeps. The rest is still at the
			<a href={data.content.url} target="_blank" rel="noreferrer" class="underline">original</a>.
		</p>
	{/if}

	{#if ordered.length}
		<section id="marked" class="mt-10 border-t pt-6" style="border-color: var(--color-border)">
			<h2 class="t-section">Marked in this article</h2>
			<ul class="mt-3 flex flex-col gap-3">
				{#each ordered as h (h.id)}
					<li class="flex items-start gap-2 text-sm">
						<div class="min-w-0 flex-1">
							{#if h.orphaned}
								<!-- The quote is kept for exactly this: the article changed, and the words
								     are still worth having even though there is nowhere to put the mark. -->
								<blockquote
									class="border-l-2 pl-3"
									style="border-color: var(--color-border); color: var(--color-muted)"
								>
									{h.text}
								</blockquote>
								<p class="mt-1 pl-3 text-xs" style="color: var(--color-muted)">
									The article has changed since you marked this, so it is no longer shown in the text.
								</p>
							{:else}
								<button
									type="button"
									onclick={() => jumpTo(h)}
									class="block w-full border-l-2 pl-3 text-left hover:underline"
									style="border-color: var(--color-highlight-strong)"
									title="Show it in the article"
								>
									{h.text}
								</button>
							{/if}
							{#if h.note}
								<p class="mt-1 whitespace-pre-line pl-3" style="color: var(--color-muted)">
									{h.note}
								</p>
							{/if}
						</div>
						<button
							type="button"
							onclick={(event) => openPanel(h.id, event.currentTarget)}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							title={h.note ? 'Edit the note' : 'Add a note'}
							aria-label={h.note ? 'Edit the note' : 'Add a note'}
						>
							<Pencil size={16} aria-hidden="true" />
						</button>
						<button
							type="button"
							onclick={() => remove(h)}
							class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
							title="Remove this highlight"
							aria-label="Remove this highlight"
						>
							<Trash2 size={16} aria-hidden="true" />
						</button>
					</li>
				{/each}
			</ul>
		</section>
	{/if}
</article>

{#if selection && toolbarAt}
	<!--
		mousedown is swallowed so pressing a button does not throw away the selection it is about
		to act on.

		Above the selection with a mouse; a bar across the bottom under a finger. Directly above a
		selection is exactly where iOS and Android draw their own Copy / Look Up callout, so the
		two sat on top of each other and neither could be used.
	-->
	<div
		class="popover-surface fixed z-30 flex items-center gap-0.5 rounded-lg border p-1 text-sm shadow-lg {touch
			? 'inset-x-3 justify-center'
			: '-translate-x-1/2 -translate-y-full'}"
		style={touch
			? 'bottom: max(0.75rem, env(safe-area-inset-bottom))'
			: `left: ${toolbarAt.x}px; top: ${toolbarAt.y - 8}px`}
		role="toolbar"
		aria-label="Highlight the selection"
		tabindex="-1"
		onmousedown={(event) => event.preventDefault()}
	>
		{#if selection.spans}
			<span class="px-2 py-1" style="color: var(--color-muted)">One paragraph at a time</span>
		{:else}
			<button
				type="button"
				onclick={() => highlight(false)}
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="Highlight (h)"
				aria-label="Highlight"
			>
				<Highlighter size={17} aria-hidden="true" />
			</button>
			<button
				type="button"
				onclick={() => highlight(true)}
				class="inline-flex items-center rounded p-1.5 hover:bg-black/5 dark:hover:bg-white/10"
				title="Highlight and add a note"
				aria-label="Highlight and add a note"
			>
				<MessageSquarePlus size={17} aria-hidden="true" />
			</button>
		{/if}
	</div>
{/if}

{#if activeHighlight && panelAt}
	<div
		class="popover-surface fixed z-30 rounded-lg border p-2 text-sm shadow-lg"
		style="left: {panelAt.x}px; top: {panelAt.y}px; width: {panelAt.width}px"
		role="dialog"
		aria-label="Note on this highlight"
	>
		<!-- svelte-ignore a11y_autofocus -->
		<Textarea
			bind:value={draft}
			rows={3}
			maxlength={1000}
			autofocus
			placeholder="Why this mattered…"
			onkeydown={(event) => {
				if (event.key === 'Escape') {
					event.preventDefault();
					closePanel();
				} else if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
					event.preventDefault();
					void saveNote();
				}
			}}
			size="sm"
			class="w-full"
		/>
		<div class="mt-1 flex items-center gap-0.5">
			<Button
				variant="ghost-danger"
				icon={Trash2}
				iconOnly
				label="Remove this highlight"
				onclick={() => remove(activeHighlight)}
			/>
			<div class="flex-1"></div>
			<Button variant="ghost" icon={X} iconOnly label="Close (Esc)" onclick={closePanel} />
			<Button
				variant="primary"
				icon={Check}
				iconOnly
				label="Save the note (Ctrl+Enter)"
				onclick={saveNote}
				disabled={(activeHighlight.note ?? '') === draft.trim()}
			/>
		</div>
	</div>
{/if}


<style>
	/* A note is marked by a line under the words, so it can be seen without opening anything. */
	mark.noted {
		text-decoration: underline dotted;
		text-underline-offset: 0.2em;
	}
</style>
