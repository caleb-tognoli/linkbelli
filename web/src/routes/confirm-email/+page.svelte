<svelte:head><title>{pageTitle('Confirm your address')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { MailCheck, MailX } from '@lucide/svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let resending = $state(false);
	let resent = $state(false);
	let resendError = $state<string | null>(null);

	// A link opened without its address — truncated by a mail client, usually — left the page with
	// nothing on it but the complaint. Typed in, it has something to send to.
	let typed = $state('');
	const address = $derived(data.email || typed.trim());

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
			body: JSON.stringify({ email: address })
		});

		resending = false;
		if (res.ok) resent = true;
		else if (res.status === 503) resendError = 'This Linkbelli has no mail configured.';
		else resendError = 'Could not send another one. Try again in a minute.';
	}
</script>

<div
	class="w-full max-w-sm rounded-card border p-6"
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
			<a href="/playlists" class="underline underline-offset-2" style="color: var(--color-accent)">
				Go to your playlists
			</a>
		</p>
	{:else}
		<h1 class="flex items-center gap-2 text-xl font-semibold">
			<MailX size={22} aria-hidden="true" style="color: var(--color-danger)" />
			That link did not work
		</h1>
		<p class="mt-2 text-sm" style="color: var(--color-muted)">{data.error}</p>

		{#if resent}
			<p class="mt-4 text-sm">Another link is on its way to {address}.</p>
		{:else}
			<form
				class="mt-4 flex flex-col gap-3"
				onsubmit={(e) => {
					e.preventDefault();
					void resend();
				}}
			>
				{#if !data.email}
					<Field label="Your email address">
						{#snippet children(f)}
							<!-- svelte-ignore a11y_autofocus -- the one field on a page that is otherwise a dead end -->
							<Input
								id={f.id}
								type="email"
								bind:value={typed}
								autocomplete="email"
								autofocus
								required
								aria-describedby={f.describedby}
							/>
						{/snippet}
					</Field>
				{/if}
				<Button
					type="submit"
					variant="primary"
					loading={resending}
					disabled={!address}
					class="self-start"
				>
					{resending ? 'Sending…' : 'Send another link'}
				</Button>
			</form>
		{/if}

		{#if resendError}
			<p class="mt-2 text-sm" style="color: var(--color-danger)" role="alert">{resendError}</p>
		{/if}

		<p class="mt-4 text-sm">
			<!-- Signing in still works: only outbound mail waits on confirmation. -->
			<a href="/" class="underline underline-offset-2" style="color: var(--color-muted)">
				Continue to Linkbelli
			</a>
			<span class="text-muted"> — your account works either way.</span>
		</p>
	{/if}
</div>
