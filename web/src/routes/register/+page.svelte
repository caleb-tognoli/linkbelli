<svelte:head><title>Create account - linkbelli</title></svelte:head>

<script lang="ts">
	import { PASSWORD_MIN, USERNAME_HINT, USERNAME_MAX, USERNAME_MIN, USERNAME_PATTERN, passwordAcceptable, usernameProblem } from '$lib/accountRules';
	import PasswordRules from '$lib/components/PasswordRules.svelte';
	import PasswordInput from '$lib/components/ui/PasswordInput.svelte';
	import { page } from '$app/state';
	import Input from '$lib/components/ui/Input.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import { enhance } from '$app/forms';
	import { UserPlus } from '@lucide/svelte';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();
	let submitting = $state(false);

	let username = $state(form?.username ?? '');
	let password = $state('');
	// Only once they have moved on: telling somebody their username is too short while they are
	// typing the third character of it is noise.
	let usernameTouched = $state(false);
	const usernameError = $derived(usernameTouched ? usernameProblem(username) : null);
	const rulesId = 'password-rules';

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
		<Field label="Username" hint={USERNAME_HINT} error={usernameError} required>
			{#snippet children(f)}
				<Input
					id={f.id}
					name="username"
					autocomplete="username"
					bind:value={username}
					onblur={() => (usernameTouched = true)}
					minlength={USERNAME_MIN}
					maxlength={USERNAME_MAX}
					pattern={USERNAME_PATTERN}
					required
					invalid={f.invalid}
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		<Field label="Email" required>
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

		<Field label="Password" required>
			{#snippet children(f)}
				<PasswordInput
					id={f.id}
					name="password"
					autocomplete="new-password"
					bind:value={password}
					minlength={PASSWORD_MIN}
					required
					aria-describedby={[f.describedby, rulesId].filter(Boolean).join(' ')}
				/>
			{/snippet}
		</Field>
		<PasswordRules {password} id={rulesId} />

		{#if form?.error}
			<p class="text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
		{/if}

		<Button
			type="submit"
			variant="primary"
			icon={UserPlus}
			loading={submitting}
			disabled={!passwordAcceptable(password) || !!usernameProblem(username)}
			class="mt-1 w-full"
		>
			{submitting ? 'Creating…' : 'Create account'}
		</Button>
	</form>

	<p class="mt-4 text-sm" style="color: var(--color-muted)">
		Already have an account? <a href={withRedirect('/login')} class="underline">Sign in</a>
	</p>
</div>
