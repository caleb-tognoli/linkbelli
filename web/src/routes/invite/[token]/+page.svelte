<svelte:head><title>{pageTitle('An invitation')}</title></svelte:head>

<script lang="ts">
	import { pageTitle } from '$lib/title';
	import Button from '$lib/components/ui/Button.svelte';
	import { page } from '$app/state';
	import { MailX, UserPlus } from '@lucide/svelte';
	import type { ActionData, PageData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	/** Said as what they can do, not as the name of a role. */
	const WHAT: Record<string, string> = {
		Viewer: 'read it',
		Contributor: 'read it and add links to it',
		Editor: 'read it, add links, and edit or remove them'
	};

	/**
	 * Where to send somebody who has no account yet.
	 *
	 * Back here afterwards, so the invitation is still in front of them — landing on the home
	 * page after signing up would leave them holding a link they have already used up half of.
	 */
	const here = $derived(encodeURIComponent(page.url.pathname));
</script>

<div
	class="w-full max-w-sm rounded-card border p-6"
	style="border-color: var(--color-border); background: var(--color-surface)"
>
	{#if !data.invite}
		<h1 class="flex items-center gap-2 text-xl font-semibold">
			<MailX size={22} aria-hidden="true" style="color: var(--color-danger)" />
			That link has expired
		</h1>
		<p class="mt-2 text-sm" style="color: var(--color-muted)">
			An invitation works once, and lasts two weeks. Ask whoever sent it for a new one.
		</p>
	{:else}
		<h1 class="flex items-center gap-2 text-xl font-semibold">
			<UserPlus size={22} aria-hidden="true" style="color: var(--color-accent)" />
			An invitation
		</h1>
		<p class="mt-2 text-sm">
			<span class="font-medium">{data.invite.invitedBy}</span> would like you to join
			<span class="font-medium">{data.invite.playlistName}</span>.
		</p>
		<p class="mt-1 text-sm" style="color: var(--color-muted)">
			You will be able to {WHAT[data.invite.role] ?? 'read it'}.
		</p>

		{#if data.signedIn}
			<form method="post" class="mt-4">
				<Button type="submit" variant="primary" class="w-full">Join this playlist</Button>
			</form>
		{:else}
			<!-- No account yet is the case this whole feature exists for, so it is the first
			     thing offered rather than the fallback. -->
			<p class="mt-4 text-sm" style="color: var(--color-muted)">
				Sign in or make an account, and you will come back here.
			</p>
			<div class="mt-2 flex gap-2">
				<Button href={`/register?redirectTo=${here}`} variant="primary" class="flex-1">
					Make an account
				</Button>
				<Button href={`/login?redirectTo=${here}`} class="flex-1">Sign in</Button>
			</div>
		{/if}

		{#if form?.error}
			<p class="mt-3 text-sm" style="color: var(--color-danger)" role="alert">{form.error}</p>
		{/if}
	{/if}
</div>
