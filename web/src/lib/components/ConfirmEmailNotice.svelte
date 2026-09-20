<script lang="ts">
	import Button from './ui/Button.svelte';
	import { api } from '$lib/api/client';
	import { failureMessage } from '$lib/api/errors';
	import { toast } from '$lib/toast.svelte';
	import { MailWarning, X } from '@lucide/svelte';

	/**
	 * A word about the address nothing can reach yet.
	 *
	 * Signing up sends a confirmation message and drops somebody straight on the dashboard without
	 * mentioning it. Until they confirm, digests and notifications go nowhere — and the only place
	 * that said so was a subsection of Settings there is no reason to visit. So people learned that
	 * their mail was silently off by never hearing from us.
	 *
	 * Dismissible, and per browser: a nag that cannot be put away becomes furniture, and this is a
	 * reminder rather than a wall.
	 */
	let { email }: { email?: string } = $props();

	const KEY = 'lb_confirm_notice_hidden';

	function hiddenAlready(): boolean {
		try {
			return localStorage.getItem(KEY) === '1';
		} catch {
			// Blocked storage. Showing it is the safer half of the mistake.
			return false;
		}
	}

	let hidden = $state(hiddenAlready());
	let sending = $state(false);

	function hide() {
		hidden = true;
		try {
			localStorage.setItem(KEY, '1');
		} catch {
			// It stays away for this page at least.
		}
	}

	async function resend() {
		if (!email) return;

		sending = true;
		const res = await api.post('/auth/resend-confirmation', { email });
		sending = false;

		if (res.ok) toast.success(`Sent. Look for it at ${email}.`);
		else toast.error(failureMessage(res, 'Could not send another one.'));
	}
</script>

{#if !hidden}
	<div
		class="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-card border border-warning px-4 py-3 text-sm"
		role="status"
	>
		<MailWarning size={18} aria-hidden="true" class="shrink-0 text-warning" />
		<p class="min-w-0 flex-1">
			Confirm {email ?? 'your address'} to turn email on. Nothing is sent until you do — look for
			the message we sent when you signed up.
		</p>
		<Button size="sm" loading={sending} onclick={resend}>
			{sending ? 'Sending…' : 'Send another'}
		</Button>
		<Button
			variant="ghost"
			size="sm"
			icon={X}
			iconOnly
			label="Hide this reminder"
			onclick={hide}
		/>
	</div>
{/if}
