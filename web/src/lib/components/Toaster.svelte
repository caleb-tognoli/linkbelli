<script lang="ts">
	import { onDestroy } from 'svelte';
	import { fly } from 'svelte/transition';
	import { prefersReducedMotion } from '$lib/motion';
	import { CircleAlert, CircleCheck, Info, X } from '@lucide/svelte';
	import { toast, type Toast } from '$lib/toast.svelte';

	/**
	 * Draws the toasts. Mounted once, in the root layout.
	 *
	 * Good news goes by itself after a few seconds, but not while the pointer or the keyboard is
	 * on it — an Undo that vanishes as you reach for it is worse than none. Errors are announced
	 * assertively and stay until dismissed.
	 */
	const timers = new Map<number, ReturnType<typeof setTimeout>>();
	const paused = new Set<number>();

	function schedule(item: Toast) {
		if (item.duration === null || timers.has(item.id) || paused.has(item.id)) return;
		timers.set(
			item.id,
			setTimeout(() => {
				timers.delete(item.id);
				toast.dismiss(item.id);
			}, item.duration)
		);
	}

	function pause(id: number) {
		paused.add(id);
		clearTimeout(timers.get(id));
		timers.delete(id);
	}

	function resume(item: Toast) {
		paused.delete(item.id);
		schedule(item);
	}

	/**
	 * Holds the stack still while somebody is reading it, or has tabbed into it.
	 *
	 * On the region rather than on each card: mouseenter does not bubble, and a card with a mouse
	 * handler and no role of its own is a card a screen reader has to be told about for no reason.
	 * Pausing the whole stack is also the truer reading of the gesture — a pointer over one toast
	 * means the toasts are being read.
	 */
	function hold(tone: 'error' | 'other', stop: boolean) {
		for (const item of toast.list) {
			const mine = tone === 'error' ? item.tone === 'error' : item.tone !== 'error';
			if (!mine) continue;
			if (stop) pause(item.id);
			else resume(item);
		}
	}

	$effect(() => {
		for (const item of toast.list) schedule(item);
		// Forget timers for toasts that are gone.
		for (const id of timers.keys()) {
			if (!toast.list.some((t) => t.id === id)) {
				clearTimeout(timers.get(id));
				timers.delete(id);
			}
		}
	});

	onDestroy(() => {
		for (const timer of timers.values()) clearTimeout(timer);
	});

	async function act(item: Toast) {
		toast.dismiss(item.id);
		await item.action?.run();
	}

	const ICONS = { info: Info, success: CircleCheck, error: CircleAlert };

	/**
	 * Arriving and leaving, rather than appearing and vanishing between frames.
	 *
	 * Dialogs, menus, popovers and the drawer all animate; toasts did not — and they appear at the
	 * far bottom of the screen, away from wherever somebody is looking, which is exactly where a
	 * little movement is what draws the eye. Cut to nothing where less motion was asked for; the
	 * CSS rule in app.css cannot reach a transition Svelte runs from script.
	 */
	const motion = $derived(prefersReducedMotion() ? { duration: 0 } : { y: 12, duration: 160 });
</script>

<div
	class="pointer-events-none fixed inset-x-0 bottom-[max(1rem,env(safe-area-inset-bottom))] z-(--z-toast) flex flex-col items-center gap-2 px-4"
>
	<!--
		News is a status (polite), a failure an alert (assertive) — and the region has to be here
		before the message is.

		The roles used to sit on each card, which is created at the same moment as its text. A live
		region that arrives already holding its content is commonly not announced at all: screen
		readers watch regions that are already in the tree for changes. Since toasts are the only
		way this app says anything happened, that meant a whole feedback channel that a screen
		reader could miss entirely. The two regions are always mounted now, and the cards go into
		them.
	-->
	<!-- svelte-ignore a11y_mouse_events_have_key_events -- focusin/focusout are the keyboard half;
	     the rule looks for focus/blur, which do not bubble up from the card to this region. -->
	<div
		class="flex w-full flex-col items-center gap-2"
		role="status"
		aria-live="polite"
		aria-atomic="false"
		onmouseover={() => hold('other', true)}
		onmouseout={() => hold('other', false)}
		onfocusin={() => hold('other', true)}
		onfocusout={() => hold('other', false)}
	>
		{#each toast.list.filter((t) => t.tone !== 'error') as item (item.id)}
			<div transition:fly={motion}>{@render card(item)}</div>
		{/each}
	</div>
	<!-- svelte-ignore a11y_mouse_events_have_key_events -- as above. -->
	<div
		class="flex w-full flex-col items-center gap-2"
		role="alert"
		aria-live="assertive"
		aria-atomic="false"
		onmouseover={() => hold('error', true)}
		onmouseout={() => hold('error', false)}
		onfocusin={() => hold('error', true)}
		onfocusout={() => hold('error', false)}
	>
		{#each toast.list.filter((t) => t.tone === 'error') as item (item.id)}
			<div transition:fly={motion}>{@render card(item)}</div>
		{/each}
	</div>
</div>

{#snippet card(item: Toast)}
	{@const Icon = ICONS[item.tone]}
	<div
		class="pointer-events-auto flex w-full max-w-md items-start gap-2.5 rounded-card border bg-surface px-3 py-2.5 text-sm shadow-dialog {item.tone ===
		'error'
			? 'border-danger'
			: 'border-border'}"
	>
		<span
			class="mt-0.5 inline-flex shrink-0 {item.tone === 'error'
				? 'text-danger'
				: item.tone === 'success'
					? 'text-success'
					: 'text-muted'}"
		>
			<Icon size={16} aria-hidden="true" />
		</span>
		<p class="min-w-0 flex-1 break-words">{item.text}</p>
		{#if item.action}
			<button
				type="button"
				onclick={() => act(item)}
				class="-my-1 shrink-0 rounded-control px-2 py-1 font-medium text-accent hover:bg-black/5 dark:hover:bg-white/10"
			>
				{item.action.label}
			</button>
		{/if}
		<button
			type="button"
			onclick={() => toast.dismiss(item.id)}
			class="-my-1 -mr-1 inline-flex size-7 shrink-0 items-center justify-center rounded-control text-muted hover:bg-black/5 dark:hover:bg-white/10"
			title="Dismiss"
			aria-label="Dismiss"
		>
			<X size={15} aria-hidden="true" />
		</button>
	</div>
{/snippet}
