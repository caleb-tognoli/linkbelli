<svelte:head><title>Choose a new password - linkbelli</title></svelte:head>

<script lang="ts">
	import { PASSWORD_MIN, passwordAcceptable } from '$lib/accountRules';
	import PasswordRules from '$lib/components/PasswordRules.svelte';
	import PasswordInput from '$lib/components/ui/PasswordInput.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { enhance } from '$app/forms';
	import { KeyRound } from '@lucide/svelte';
	import type { ActionData, PageData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let password = $state('');
	const rulesId = 'password-rules';
	let submitting = $state(false);
</script>

<div
	class="w-full max-w-sm rounded-xl border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	<h1 class="text-xl font-semibold">Choose a new password</h1>

	{#if !data.email || !data.token}
		<!-- A truncated link, or somebody arriving here directly. Nothing useful to show. -->
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			This link is missing something — mail clients sometimes break long ones. Ask for a new
			one and it will work.
		</p>
		<p class="mt-4 text-sm">
			<a href="/forgot-password" class="underline underline-offset-2" style="color: var(--color-accent)">
				Send another link
			</a>
		</p>
	{:else}
		<p class="mt-1 text-sm" style="color: var(--color-muted)">For {data.email}.</p>

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
			<input type="hidden" name="email" value={data.email} />
			<input type="hidden" name="token" value={data.token} />

			<Field label="New password" required>
				{#snippet children(f)}
				<!-- svelte-ignore a11y_autofocus -- the first field of the form this page exists for -->
					<PasswordInput
						id={f.id}
						name="password"
						autocomplete="new-password"
						autofocus
						bind:value={password}
						minlength={PASSWORD_MIN}
						required
						aria-describedby={[f.describedby, rulesId].filter(Boolean).join(' ')}
					/>
				{/snippet}
			</Field>
			<PasswordRules {password} id={rulesId} />

			<Field label="Again, to be sure" required>
				{#snippet children(f)}
					<PasswordInput
						id={f.id}
						name="confirm"
						autocomplete="new-password"
						required
						aria-describedby={f.describedby}
					/>
				{/snippet}
			</Field>

			{#if form?.error}
				<p class="text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
			{/if}

			<Button
				type="submit"
				variant="primary"
				icon={KeyRound}
				loading={submitting}
				disabled={!passwordAcceptable(password)}
				class="mt-1 w-full"
			>
				{submitting ? 'Saving…' : 'Save it'}
			</Button>
		</form>
	{/if}
</div>
