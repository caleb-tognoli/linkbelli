<svelte:head><title>Reset your password - linkbelli</title></svelte:head>

<script lang="ts">
	import { enhance } from '$app/forms';
	import { Mail } from '@lucide/svelte';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();
	let submitting = $state(false);
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	{#if form?.sent}
		<h1 class="text-xl font-semibold">Check your email</h1>
		<!-- Careful not to confirm the account exists: this is the same message either way. -->
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			If there is an account for that, a link to choose a new password is on its way. It works
			once, and stops working after two hours.
		</p>
		<p class="mt-4 text-sm">
			<a href="/login" class="underline underline-offset-2" style="color: var(--color-accent)">
				Back to signing in
			</a>
		</p>
	{:else}
		<h1 class="text-xl font-semibold">Reset your password</h1>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			Tell us who you are and we will send a link.
		</p>

		<form
			method="post"
			class="mt-5 flex flex-col gap-3"
			use:enhance={() => {
				submitting = true;
				return async ({ update }) => {
					await update();
					submitting = false;
				};
			}}
		>
			<label class="flex flex-col gap-1 text-sm">
				<span>Username or email</span>
				<input
					name="login"
					autocomplete="username"
					value={form?.login ?? ''}
					required
					class="rounded-md border px-3 py-2"
					style="border-color: var(--color-border); background: var(--color-bg)"
				/>
			</label>

			{#if form?.error}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
			{/if}

			<button
				type="submit"
				disabled={submitting}
				class="mt-1 flex items-center justify-center gap-2 rounded-md px-3 py-2 font-medium disabled:opacity-60"
				style="background: var(--color-accent-solid); color: var(--color-on-solid)"
			>
				<Mail size={18} aria-hidden="true" />
				{submitting ? 'Sending…' : 'Send the link'}
			</button>
		</form>

		<p class="mt-4 text-sm" style="color: var(--color-muted)">
			Remembered it? <a href="/login" class="underline">Sign in</a>
		</p>
	{/if}
</div>
