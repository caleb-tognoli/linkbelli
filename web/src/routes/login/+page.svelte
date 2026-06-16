<script lang="ts">
	import { enhance } from '$app/forms';
	import { LogIn } from '@lucide/svelte';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();
	let submitting = $state(false);
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<h1 class="text-xl font-semibold">Sign in</h1>
	<p class="mt-1 text-sm" style="color: var(--color-muted)">Welcome back to Linkbelli.</p>

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

		<label class="flex flex-col gap-1 text-sm">
			<span>Password</span>
			<input
				name="password"
				type="password"
				autocomplete="current-password"
				required
				class="rounded-md border px-3 py-2"
				style="border-color: var(--color-border); background: var(--color-bg)"
			/>
		</label>

		{#if form?.error}
			<p class="text-sm" style="color: var(--color-danger)">{form.error}</p>
		{/if}

		<button
			type="submit"
			disabled={submitting}
			class="mt-1 flex items-center justify-center gap-2 rounded-md px-3 py-2 font-medium disabled:opacity-60"
			style="background: var(--color-accent); color: var(--color-accent-contrast)"
		>
			<LogIn size={18} aria-hidden="true" />
			{submitting ? 'Signing in…' : 'Sign in'}
		</button>
	</form>

	<p class="mt-4 text-sm" style="color: var(--color-muted)">
		No account? <a href="/register" class="underline">Create one</a>
	</p>
</div>
