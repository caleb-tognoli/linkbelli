<svelte:head><title>Create account - linkbelli</title></svelte:head>

<script lang="ts">
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
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
		<Field label="Username">
			{#snippet children(f)}
				<Input
					id={f.id}
					name="username"
					autocomplete="username"
					value={form?.username ?? ''}
					required
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		<Field label="Email">
			{#snippet children(f)}
				<Input
					id={f.id}
					name="email"
					type="email"
					autocomplete="email"
					value={form?.email ?? ''}
					required
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		<Field label="Password">
			{#snippet children(f)}
				<Input
					id={f.id}
					name="password"
					type="password"
					autocomplete="new-password"
					required
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		{#if form?.error}
			<p class="text-sm" style="color: var(--color-danger)">{form.error}</p>
		{/if}

		<Button type="submit" variant="primary" icon={UserPlus} loading={submitting} class="mt-1 w-full">
			{submitting ? 'Creating…' : 'Create account'}
		</Button>
	</form>

	<p class="mt-4 text-sm" style="color: var(--color-muted)">
		Already have an account? <a href="/login" class="underline">Sign in</a>
	</p>
</div>
