<script lang="ts">
	import { enhance } from '$app/forms';
	import { UserPlus } from '@lucide/svelte';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();
	let submitting = $state(false);
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<h1 class="text-xl font-semibold">Create account</h1>
	<p class="mt-1 text-sm" style="color: var(--color-muted)">Start grouping links with Linkbelli.</p>

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
			<span>Username</span>
			<input
				name="username"
				autocomplete="username"
				value={form?.username ?? ''}
				required
				class="rounded-md border px-3 py-2"
				style="border-color: var(--color-border); background: var(--color-bg)"
			/>
		</label>

		<label class="flex flex-col gap-1 text-sm">
			<span>Email</span>
			<input
				name="email"
				type="email"
				autocomplete="email"
				value={form?.email ?? ''}
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
				autocomplete="new-password"
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
			<UserPlus size={16} aria-hidden="true" />
			{submitting ? 'Creating…' : 'Create account'}
		</button>
	</form>

	<p class="mt-4 text-sm" style="color: var(--color-muted)">
		Already have an account? <a href="/login" class="underline">Sign in</a>
	</p>
</div>
