<script lang="ts">
	import { onDestroy } from 'svelte';
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
</script>

<div
	class="pointer-events-none fixed inset-x-0 bottom-[max(1rem,env(safe-area-inset-bottom))] z-(--z-toast) flex flex-col items-center gap-2 px-4"
>
	<!-- News is a status (polite), a failure an alert (assertive), each card its own region. -->
	<div class="flex w-full flex-col items-center gap-2">
		{#each toast.list.filter((t) => t.tone !== 'error') as item (item.id)}
			{@render card(item)}
		{/each}
	</div>
	<div class="flex w-full flex-col items-center gap-2">
		{#each toast.list.filter((t) => t.tone === 'error') as item (item.id)}
			{@render card(item)}
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
		role={item.tone === 'error' ? 'alert' : 'status'}
		onmouseenter={() => pause(item.id)}
		onmouseleave={() => resume(item)}
		onfocusin={() => pause(item.id)}
		onfocusout={() => resume(item)}
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
