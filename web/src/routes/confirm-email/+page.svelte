<svelte:head><title>Confirm your address - linkbelli</title></svelte:head>

<script lang="ts">
	import { MailCheck, MailX } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let resending = $state(false);
	let resent = $state(false);
	let resendError = $state<string | null>(null);

	/**
	 * Asks for another link.
	 *
	 * The answer is the same whether or not that address has an account, so there is nothing to
	 * branch on — the message below says what was done, not what was found.
	 */
	async function resend() {
		resending = true;
		resendError = null;

		const res = await fetch('/api/v1/auth/resend-confirmation', {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify({ email: data.email })
		});

		resending = false;
		if (res.ok) resent = true;
		else if (res.status === 503) resendError = 'This Linkbelli has no mail configured.';
		else resendError = 'Could not send another one. Try again in a minute.';
	}
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	{#if data.ok}
		<h1 class="flex items-center gap-2 text-xl font-semibold">
			<MailCheck size={22} aria-hidden="true" style="color: var(--color-accent)" />
			Address confirmed
		</h1>
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			{data.email} is yours. Digests and notifications can reach you now.
		</p>
		<p class="mt-4 text-sm">
			<a href="/" class="underline underline-offset-2" style="color: var(--color-accent)">
				Go to your playlists
			</a>
		</p>
	{:else}
		<h1 class="flex items-center gap-2 text-xl font-semibold">
			<MailX size={22} aria-hidden="true" style="color: var(--color-danger)" />
			That link did not work
		</h1>
		<p class="mt-2 text-sm" style="color: var(--color-muted)">{data.error}</p>

		{#if data.email}
			{#if resent}
				<p class="mt-4 text-sm">Another link is on its way to {data.email}.</p>
			{:else}
				<button
					type="button"
					onclick={resend}
					disabled={resending}
					class="mt-4 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-60"
					style="background: var(--color-accent-solid); color: var(--color-on-solid)"
				>
					{resending ? 'Sending…' : 'Send another link'}
				</button>
			{/if}

			{#if resendError}
				<p class="mt-2 text-sm" style="color: var(--color-danger)" role="alert">{resendError}</p>
			{/if}
		{/if}

		<p class="mt-4 text-sm">
			<!-- Signing in still works: only outbound mail waits on confirmation. -->
			<a href="/" class="underline underline-offset-2" style="color: var(--color-muted)">
				Your account works either way
			</a>
		</p>
	{/if}
</div>
