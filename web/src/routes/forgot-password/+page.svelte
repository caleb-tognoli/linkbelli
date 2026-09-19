<svelte:head><title>Reset your password - linkbelli</title></svelte:head>

<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
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
			<Field label="Username or email">
				{#snippet children(f)}
					<Input
						id={f.id}
						name="login"
						autocomplete="username"
						value={form?.login ?? ''}
						required
						aria-describedby={f.describedby}
					/>
				{/snippet}
			</Field>

			{#if form?.error}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
			{/if}

			<Button type="submit" variant="primary" icon={Mail} loading={submitting} class="mt-1 w-full">
				{submitting ? 'Sending…' : 'Send the link'}
			</Button>
		</form>

		<p class="mt-4 text-sm" style="color: var(--color-muted)">
			Remembered it? <a href="/login" class="underline">Sign in</a>
		</p>
	{/if}
</div>
