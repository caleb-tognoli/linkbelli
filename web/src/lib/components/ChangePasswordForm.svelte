<script lang="ts">
	import { KeyRound } from '@lucide/svelte';
	import Button from '$lib/components/ui/Button.svelte';
	import Field from '$lib/components/ui/Field.svelte';
	import PasswordInput from '$lib/components/ui/PasswordInput.svelte';
	import PasswordRules from '$lib/components/PasswordRules.svelte';
	import { PASSWORD_MIN, passwordAcceptable } from '$lib/accountRules';
	import { api } from '$lib/api/client';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';

	/**
	 * Changing a password without signing out.
	 *
	 * There was no way to do it: the only route was the signed-out reset flow, which a signed-in
	 * visitor is redirected away from — so rotating a password typed on somebody else's machine
	 * meant signing out and claiming to have forgotten it.
	 */
	let current = $state('');
	let next = $state('');
	let saving = $state(false);
	let currentError = $state<string | null>(null);
	let nextError = $state<string | null>(null);

	const rulesId = 'change-password-rules';

	async function save(event: SubmitEvent) {
		event.preventDefault();
		currentError = null;
		nextError = null;

		if (!passwordAcceptable(next)) {
			nextError = 'That password does not meet all of the rules below.';
			return;
		}

		saving = true;
		const res = await api.post('/me/password', { currentPassword: current, newPassword: next });
		saving = false;

		if (res.ok || res.status === 204) {
			current = '';
			next = '';
			toast.success('Your password is changed.');
			return;
		}

		// The API says which of the two it means: a wrong current password and a refused new one
		// are different problems, and one message for both leaves somebody retyping a good one.
		const problem = (await res.json().catch(() => null)) as {
			errors?: Record<string, string[]>;
		} | null;
		const errors = problem?.errors ?? {};
		currentError = errors.currentPassword?.[0] ?? null;
		nextError = errors.newPassword?.[0] ?? null;
		if (!currentError && !nextError) {
			nextError = failureMessage(res.status, 'Could not change your password.');
		}
	}
</script>

<div>
	<h3 class="t-section">Password</h3>
	<form class="mt-3 flex max-w-sm flex-col gap-3" onsubmit={save} novalidate>
		<Field label="Current password" error={currentError} required>
			{#snippet children(f)}
				<PasswordInput
					id={f.id}
					bind:value={current}
					autocomplete="current-password"
					required
					invalid={f.invalid}
					aria-describedby={f.describedby}
				/>
			{/snippet}
		</Field>

		<Field label="New password" error={nextError} required>
			{#snippet children(f)}
				<PasswordInput
					id={f.id}
					bind:value={next}
					autocomplete="new-password"
					minlength={PASSWORD_MIN}
					required
					invalid={f.invalid}
					aria-describedby={[f.describedby, rulesId].filter(Boolean).join(' ')}
				/>
			{/snippet}
		</Field>

		<PasswordRules password={next} id={rulesId} />

		<Button
			type="submit"
			icon={KeyRound}
			loading={saving}
			disabled={!current || !passwordAcceptable(next)}
			class="self-start"
		>
			{saving ? 'Changing…' : 'Change password'}
		</Button>
		<p class="text-xs text-muted">
			Other devices signed in as you are signed out the next time they ask for a new session.
		</p>
	</form>
</div>
