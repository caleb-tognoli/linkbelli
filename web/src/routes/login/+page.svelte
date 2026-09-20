<svelte:head><title>Sign in - linkbelli</title></svelte:head>

<script lang="ts">
	import PasswordInput from '$lib/components/ui/PasswordInput.svelte';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { enhance } from '$app/forms';
	import { LogIn } from '@lucide/svelte';
	import { page } from '$app/state';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();
	let submitting = $state(false);

	// Set by the reset flow, which deliberately does not sign anybody in — so this is the one
	// place that can confirm the new password took.
	const justReset = $derived(page.url.searchParams.get('reset') === '1');

	// Carried across to the other form, so whichever one somebody ends up using still takes them
	// where they were going.
	function withRedirect(path: string) {
		const next = page.url.searchParams.get('redirectTo');
		return next ? `${path}?redirectTo=${encodeURIComponent(next)}` : path;
	}
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<h1 class="text-xl font-semibold">Sign in</h1>
	{#if justReset}
		<p class="mt-1 text-sm" style="color: var(--color-success)">
			Your password is changed. Sign in with the new one.
		</p>
	{:else}
		<p class="mt-1 text-sm" style="color: var(--color-muted)">Welcome back to Linkbelli.</p>
	{/if}

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

		<Field label="Password">
			{#snippet children(f)}
				<PasswordInput
					id={f.id}
					name="password"
					autocomplete="current-password"
					required
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		{#if form?.error}
			<p class="text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
		{/if}

		<Button type="submit" variant="primary" icon={LogIn} loading={submitting} class="mt-1 w-full">
			{submitting ? 'Signing in…' : 'Sign in'}
		</Button>
	</form>

	<p class="mt-4 text-sm" style="color: var(--color-muted)">
		No account? <a href={withRedirect('/register')} class="underline">Create one</a>
		· <a href="/forgot-password" class="underline">Forgotten your password?</a>
	</p>
</div>
